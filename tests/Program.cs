using System.Xml.Linq;
using IlPostPodcastProxy;

var feed = """
    <?xml version="1.0"?><rss><channel><image><url>old.jpg</url></image>
    <item><link>https://www.ilpost.it/podcasts/morning/ultima-puntata/</link><guid>https://www.ilpost.it/?p=101</guid></item>
    <item><link>https://www.ilpost.it/podcasts/morning/older/</link><guid>https://www.ilpost.it/?p=102</guid></item>
    <item><guid>https://www.ilpost.it/?p=103</guid></item></channel></rss>
    """;
var page = "<div class='_podcast-header__image_10bfc_29'><img src='https://cdn.example/cover.jpg?a=1&amp;b=2'></div>";
var json = """
    {"data":{"episode":{"data":[{"id":101,"episode_raw_url":"https://cdn.example/first.mp3"}]},"related":{"data":[{"id":102,"episode_raw_url":"https://cdn.example/second.mp3"},{"id":103,"episode_raw_url":null}]}}}
    """;

Assert(IlPostClient.GetLatestEpisodeName(feed) == "ultima-puntata", "latest episode slug");
var urls = IlPostClient.ParseEpisodeUrls(json);
Assert(urls.Count == 2 && urls["101"] == "https://cdn.example/first.mp3" && urls["102"] == "https://cdn.example/second.mp3", "episode and related URLs");
var result = XDocument.Parse(IlPostClient.EnrichFeed(feed, page, urls));
var channel = result.Root!.Element("channel")!;
XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
Assert(channel.Element(itunes + "block")?.Value == "yes", "private feed marker");
Assert(channel.Element("image")?.Element("url")?.Value == "https://cdn.example/cover.jpg?a=1&b=2", "podcast artwork");
var items = channel.Elements("item").ToArray();
Assert(items[0].Element("enclosure")?.Attribute("url")?.Value == urls["101"], "latest enclosure");
Assert(items[1].Element("enclosure")?.Attribute("url")?.Value == urls["102"], "related enclosure");
Assert(items[2].Element("enclosure") == null, "no enclosure without audio URL");
Console.WriteLine("All podcast feed checks passed.");

static void Assert(bool condition, string message) {
    if (!condition) throw new Exception($"Failed: {message}");
}
