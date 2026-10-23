using ApiGateway;
using ApiGateway.Middlewares;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Requester;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

// Project-CA cert: Inventory validates the gateway against it.
var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(builder.Configuration["MutualTls:ServerCertificatePath"]!, password: null);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false; // No Server header.
    options.Limits.MaxRequestBodySize = 2 * 1024 * 1024; // Default ~28 MB; 2 MB fits a base64 product image.
    options.ConfigureHttpsDefaults(https => https.ServerCertificate = serverCertificate);
});

builder.Configuration.AddJsonFile(path: "ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddOcelot().AddDelegatingHandler<HideDownstreamErrorsHandler>(global: true);
builder.Services.AddSingleton<IMessageInvokerPool, MtlsMessageInvokerPool>(); // mTLS to services.

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddIdentitySettings();
builder.Services.AddAuthRateLimiting();

var app = builder.Build();

// Strip stack-revealing headers (incl. downstream copies); add security headers.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.XContentTypeOptions = "nosniff";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers.CacheControl = "no-store"; // May hold personal data.
        return Task.CompletedTask;
    });
    await next();
});

// Explicit, after the header middleware; otherwise auth runs first and its 401 crashes OnStarting.
app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<ErrorHandlingMiddleware>();
await app.UseOcelot();
app.Run();
