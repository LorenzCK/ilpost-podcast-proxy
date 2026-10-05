using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using RestSharp;

namespace IlPostPodcastProxy {
    internal class IlPostClient {
        private static readonly Uri LoginUrl = new("https://www.ilpost.it/wp-login.php");
        private static readonly Uri SiteUrl = new("https://www.ilpost.it/");
        private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        private readonly IConfiguration _configuration;
        private readonly RestClient _restClient;
        private readonly CookieContainer _cookies = new();
        private bool _loggedIn;

        public IlPostClient(IConfiguration configuration, RestSharpBodyDumperInterceptor interceptor) {
            _configuration = configuration;

            _restClient = new RestClient(new RestClientOptions {
                CookieContainer = _cookies,
                Interceptors = { interceptor },
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:156.0) Gecko/20100101 Firefox/156.0",
            });
            interceptor.Cookies = _cookies;
            _restClient.AddDefaultHeader("Accept-Language", "it;q=0.9,en-US,en;q=0.9");
            _restClient.AddDefaultHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }

        public async Task LoginAsync(CancellationToken cancellationToken = default) {
            var creds = _configuration.GetRequiredSection("Credentials");
            var username = creds["Username"];
            var password = creds["Password"];
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) {
                throw new InvalidOperationException("Credentials:Username and Credentials:Password are required.");
            }

            _loggedIn = false;

            // WordPress sets wordpress_test_cookie when the login form is loaded. It must be
            // sent back with the POST, so prime the shared CookieContainer first.
            var loginPage = await _restClient.ExecuteAsync(new RestRequest(LoginUrl), cancellationToken);
            RequireContent(loginPage, "login page");

            var request = new RestRequest(LoginUrl, Method.Post);
            request.AddHeader("Referer", "https://www.ilpost.it/wp-login.php");
            request.AddParameter("log", username);
            request.AddParameter("pwd", password);
            request.AddParameter("rememberme", "forever");
            request.AddParameter("wp-submit", "Login");
            request.AddParameter("redirect_to", "https://www.ilpost.it/wp-admin/profile.php");
            request.AddParameter("testcookie", "1");

            var response = await _restClient.ExecuteAsync(request, cancellationToken);
            RequireContent(response, "login");
            // A 200 response can still be the WordPress login form (invalid credentials).
            if (!_cookies.GetCookies(SiteUrl).Cast<Cookie>().Any(cookie => cookie.Name.StartsWith("wordpress_logged_in_", StringComparison.Ordinal))) {
                throw new InvalidOperationException("Il Post login did not establish an authenticated session.");
            }

            _loggedIn = true;
        }

        public async Task<string> GetPodcastPageAsync(string podcast, CancellationToken cancellationToken = default) {
            RequireLogin();
            return await GetContentAsync(PodcastUrl(podcast), "podcast page", cancellationToken);
        }

        public async Task<string> GetFeedAsync(string podcast, CancellationToken cancellationToken = default) {
            RequireLogin();
            return await GetContentAsync(new Uri(PodcastUrl(podcast), "feed/"), "podcast feed", cancellationToken);
        }

        public async Task<IReadOnlyDictionary<string, string>> GetEpisodeUrlsAsync(string podcast, string lastEpisodeName, CancellationToken cancellationToken = default) {
            RequireLogin();
            ValidateSegment(podcast, nameof(podcast));
            ValidateSegment(lastEpisodeName, nameof(lastEpisodeName));
            var url = new Uri($"https://api-prod.ilpost.it/podcast/v1/bff/podcast/{Uri.EscapeDataString(podcast)}/{Uri.EscapeDataString(lastEpisodeName)}/");
            var content = await GetContentAsync(url, "episode details", cancellationToken);
            return ParseEpisodeUrls(content);
        }

        internal static IReadOnlyDictionary<string, string> ParseEpisodeUrls(string content) {
            using var json = JsonDocument.Parse(content);
            var urls = new Dictionary<string, string>();
            var data = json.RootElement.GetProperty("data");
            foreach (var collection in new[] { data.GetProperty("episode").GetProperty("data"), data.GetProperty("related").GetProperty("data") }) {
                IEnumerable<JsonElement> episodes = collection.ValueKind == JsonValueKind.Array ? collection.EnumerateArray().ToArray() : [collection];
                foreach (var episode in episodes) {
                    if (episode.TryGetProperty("id", out var id) && episode.TryGetProperty("episode_raw_url", out var rawUrl)) {
                        var key = id.ToString();
                        var value = rawUrl.ValueKind == JsonValueKind.String ? rawUrl.GetString() : null;
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value)) {
                            urls[key] = value;
                        }
                    }
                }
            }
            return urls;
        }

        public async Task<string> GetEnrichedFeedAsync(string podcast, CancellationToken cancellationToken = default) {
            var feed = await GetFeedAsync(podcast, cancellationToken);
            var lastEpisodeName = GetLatestEpisodeName(feed);
            var urls = await GetEpisodeUrlsAsync(podcast, lastEpisodeName, cancellationToken);
            var page = await GetPodcastPageAsync(podcast, cancellationToken);
            return EnrichFeed(feed, page, urls);
        }

        internal static string GetLatestEpisodeName(string feed) {
            var channel = XDocument.Parse(feed).Root?.Element("channel") ?? throw new FormatException("Podcast feed has no channel.");
            var firstLink = channel.Elements("item").FirstOrDefault()?.Element("link")?.Value
                ?? throw new FormatException("Podcast feed has no episode link.");
            var path = new Uri(firstLink, UriKind.Absolute).AbsolutePath.TrimEnd('/');
            return Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        }

        internal static string EnrichFeed(string feedXml, string page, IReadOnlyDictionary<string, string> urls) {
            var feed = XDocument.Parse(feedXml);
            var channel = feed.Root?.Element("channel") ?? throw new FormatException("Podcast feed has no channel.");
            var image = Regex.Match(page, "<div\\b[^>]*class\\s*=\\s*['\"][^'\"]*_podcast-header__image[^'\"]*['\"][^>]*>.*?<img\\b[^>]*src\\s*=\\s*['\"](?<src>[^'\"]+)['\"]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (image.Success && channel.Element("image")?.Element("url") is XElement imageUrl) {
                imageUrl.Value = WebUtility.HtmlDecode(image.Groups["src"].Value);
            }
            channel.AddFirst(new XElement(Itunes + "block", "yes"));
            foreach (var item in channel.Elements("item")) {
                var guid = item.Element("guid")?.Value;
                if (guid == null || !Uri.TryCreate(guid, UriKind.Absolute, out var guidUri)) continue;
                var query = guidUri.Query.TrimStart('?').Split('&');
                var id = query.Select(part => part.Split('=', 2)).FirstOrDefault(parts => parts.Length == 2 && parts[0] == "p")?.LastOrDefault();
                if (id != null && urls.TryGetValue(Uri.UnescapeDataString(id), out var url) && !string.IsNullOrEmpty(url)) {
                    item.Add(new XElement("enclosure", new XAttribute("url", url), new XAttribute("type", "audio/mpeg")));
                }
            }
            return feed.Declaration == null ? feed.ToString() : feed.Declaration + Environment.NewLine + feed;
        }

        private async Task<string> GetContentAsync(Uri url, string operation, CancellationToken cancellationToken) {
            var response = await _restClient.ExecuteAsync(new RestRequest(url), cancellationToken);
            return RequireContent(response, operation);
        }

        private static string RequireContent(RestResponse response, string operation) {
            if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content)) {
                throw new HttpRequestException($"Il Post {operation} failed ({response.StatusCode}, {response.ResponseStatus}).", response.ErrorException, response.StatusCode);
            }
            return response.Content;
        }

        private void RequireLogin() {
            if (!_loggedIn) throw new InvalidOperationException("Log in before requesting a podcast feed.");
        }

        private static Uri PodcastUrl(string podcast) {
            ValidateSegment(podcast, nameof(podcast));
            return new Uri(SiteUrl, $"podcasts/{Uri.EscapeDataString(podcast)}/");
        }

        private static void ValidateSegment(string segment, string name) {
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains('/') || segment.Contains('\\')) {
                throw new ArgumentException("Expected a single non-empty URL path segment.", name);
            }
        }
    }
}
