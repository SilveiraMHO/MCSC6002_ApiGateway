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

namespace ApiGateway
{
    public static class DependencyInjection
    {
        internal static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            // RS256: the gateway only has the public key, so it can verify tokens but not create them.
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(configuration["TokenSettings:PublicKeyPath"]
                ?? throw new InvalidOperationException("TokenSettings:PublicKeyPath is not configured.")));

            // Same configuration of IdentityService.
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
                                // Bad signature, wrong algorithm, malformed token: client error, not server error.
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
    }
}
