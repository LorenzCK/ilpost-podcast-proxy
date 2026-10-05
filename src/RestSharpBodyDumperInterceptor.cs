using Microsoft.Extensions.Logging;
using RestSharp;
using RestSharp.Interceptors;

namespace IlPostPodcastProxy {
    internal class RestSharpBodyDumperInterceptor : Interceptor {
        private readonly ILogger<RestSharpBodyDumperInterceptor> _logger;

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

            _logger.LogDebug("Response {StatusDescription} {StatusCode}, {ContentLength} bytes of type {ContentType}",
                response.StatusDescription, (int)response.StatusCode, response.ContentLength ?? 0, response.ContentType);

            return ValueTask.CompletedTask;
        }
    }
}
