using Microsoft.Extensions.Logging;
using RestSharp;
using RestSharp.Interceptors;
using System.Net;

namespace IlPostPodcastProxy {
    internal class RestSharpBodyDumperInterceptor : Interceptor {
        private readonly ILogger<RestSharpBodyDumperInterceptor> _logger;

        public CookieContainer? Cookies { get; set; }

        public RestSharpBodyDumperInterceptor(ILogger<RestSharpBodyDumperInterceptor> logger) {
            _logger = logger;
        }

        public override ValueTask BeforeHttpRequest(HttpRequestMessage requestMessage, CancellationToken cancellationToken) {
            if(requestMessage == null) {
                return ValueTask.CompletedTask;
            }

            _logger.LogDebug("Request {Method} {Url}", requestMessage.Method, requestMessage.RequestUri);

            return ValueTask.CompletedTask;
        }

        public override ValueTask AfterRequest(RestResponse response, CancellationToken cancellationToken) {
            if (response == null) {
                return ValueTask.CompletedTask;
            }

            var effectiveContentLength = response.RawBytes?.Length ?? (response.Content is { } content ? System.Text.Encoding.UTF8.GetByteCount(content) : 0);
            _logger.LogDebug("Response {StatusCode} {StatusDescription}, {ContentLength} bytes of type {ContentType}{CookieInfo}",
                (int)response.StatusCode, response.StatusDescription, effectiveContentLength, response.ContentType,
                Cookies != null ? $" with {Cookies.Count} cookies" : string.Empty);

            return ValueTask.CompletedTask;
        }
    }
}
