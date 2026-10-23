using ResponseLibrary.Wrappers;
using System.Text;
using System.Text.Json;

namespace ApiGateway.Middlewares
{
    // Ocelot copies downstream bodies to the client as-is. A service 5xx can carry a stack trace (and the caller's
    // headers, in Development), so the body is replaced with a generic message; the status code is kept.
    public class HideDownstreamErrorsHandler : DelegatingHandler
    {
        private readonly ILogger<HideDownstreamErrorsHandler> _logger;

        public HideDownstreamErrorsHandler(ILogger<HideDownstreamErrorsHandler> logger)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode >= 500)
            {
                // The service logs its own exception; the gateway only records which call failed.
                _logger.LogWarning("Downstream {StatusCode} for {Method} {Path}", (int)response.StatusCode, request.Method, request.RequestUri?.AbsolutePath);

                var responseWrapper = await ResponseWrapper.FailAsync();
                responseWrapper.Messages = ["An internal error occurred."];

                response.Content.Dispose();
                response.Content = new StringContent(JsonSerializer.Serialize(responseWrapper), Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}
