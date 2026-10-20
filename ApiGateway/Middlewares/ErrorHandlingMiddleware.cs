using Ocelot.Configuration;
using ResponseLibrary.Exceptions;
using ResponseLibrary.Wrappers;
using System.Net;
using System.Text.Json;

namespace ApiGateway.Middlewares
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);

                if (context.Response.StatusCode == StatusCodes.Status502BadGateway)
                {
                    var downstreamDomain = GetDownstreamDomain(context);

                    throw new ServiceUnavailableException($"{downstreamDomain} - Service Unavailable");
                }
            }
            catch (Exception ex)
            {
                var response = context.Response;
                response.ContentType = "application/json";

                var responseWrapper = await ResponseWrapper.FailAsync();

                switch (ex)
                {
                    case ServiceUnavailableException suex:
                        response.StatusCode = (int)suex.StatusCode;
                        responseWrapper.Messages = [suex.FriendlyErrorMessage];
                        break;
                    default:
                        response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        responseWrapper.Messages = [ex.Message];
                        break;
                }

                var result = JsonSerializer.Serialize(responseWrapper);

                await response.WriteAsync(result);
            }
        }

        private static string GetDownstreamDomain(HttpContext context)
        {
            if (context.Items.TryGetValue("DownstreamRoute", out var downstreamRouteObj) && downstreamRouteObj is DownstreamRoute downstreamRoute)
            {
                var scheme = downstreamRoute.DownstreamScheme;
                var hostAndPort = downstreamRoute.DownstreamAddresses[0];

                if (hostAndPort is not null)
                {
                    return $"{scheme}://{hostAndPort.Host}:{hostAndPort.Port}";
                }
            }

            return "UnknownService";
        }
    }
}
