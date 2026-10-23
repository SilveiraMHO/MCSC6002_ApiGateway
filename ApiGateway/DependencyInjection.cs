using AuthLibrary.Constants.Authentication;
using AuthLibrary.Permissions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using ResponseLibrary.Wrappers;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace ApiGateway
{
    public static class DependencyInjection
    {
        internal static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            // RS256: public key only, verify only.
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(configuration["TokenSettings:PublicKeyPath"]
                ?? throw new InvalidOperationException("TokenSettings:PublicKeyPath is not configured.")));

            services
                .AddAuthentication(auth =>
                {
                    auth.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    auth.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer("Bearer", bearer =>
                {
                    bearer.RequireHttpsMetadata = false;
                    bearer.SaveToken = true;
                    bearer.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidIssuer = AppClaim.Issuer,
                        ValidAudience = AppClaim.Audience,
                        RoleClaimType = ClaimTypes.Role,
                        ClockSkew = TimeSpan.Zero,
                        IssuerSigningKey = new RsaSecurityKey(rsa),
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                    };
                    bearer.Events = new JwtBearerEvents
                    {
                        OnAuthenticationFailed = context =>
                        {
                            if (context.Exception is SecurityTokenExpiredException)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                                context.Response.ContentType = "application/json";

                                var result = JsonSerializer.Serialize(ResponseWrapper.Fail("The token is expired."));

                                return context.Response.WriteAsync(result);
                            }
                            else
                            {
                                // Invalid token: 401, not 500.
                                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                                context.Response.ContentType = "application/json";

                                var result = JsonSerializer.Serialize(ResponseWrapper.Fail("Invalid token."));

                                return context.Response.WriteAsync(result);
                            }
                        },
                        OnChallenge = context =>
                        {
                            context.HandleResponse();

                            if (!context.Response.HasStarted)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                                context.Response.ContentType = "application/json";

                                var result = JsonSerializer.Serialize(ResponseWrapper.Fail("You are not Authorized."));

                                return context.Response.WriteAsync(result);
                            }

                            return Task.CompletedTask;
                        },
                        OnForbidden = context =>
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                            context.Response.ContentType = "application/json";

                            var result = JsonSerializer.Serialize(ResponseWrapper.Fail("You are not authorized to access this resource."));

                            return context.Response.WriteAsync(result);
                        }
                    };
                });

            return services;
        }

        internal static IServiceCollection AddIdentitySettings(this IServiceCollection services)
        {
            services
                .AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>()
                .AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

            return services;
        }

        // 5 auth calls/min per IP. Not Ocelot's limiter: it keys on a caller-controlled header.
        internal static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
        {
            return services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.Request.Path.StartsWithSegments("/auth")
                        ? RateLimitPartition.GetFixedWindowLimiter(
                            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", // Limitation: behind a proxy needs ForwardedHeaders.
                            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) })
                        : RateLimitPartition.GetNoLimiter(string.Empty));

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, cancellationToken) =>
                {
                    context.HttpContext.Response.ContentType = "application/json";
                    var result = JsonSerializer.Serialize(ResponseWrapper.Fail("Too many attempts. Try again later."));
                    return new ValueTask(context.HttpContext.Response.WriteAsync(result, cancellationToken));
                };
            });
        }
    }
}
