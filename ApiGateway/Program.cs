using ApiGateway;
using ApiGateway.Middlewares;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Requester;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

// HTTPS certificate issued by the project CA, so InventoryService can validate the gateway without the OS store.
var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(builder.Configuration["MutualTls:ServerCertificatePath"]!, password: null);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false; // Do not advertise the web server (Server: Kestrel) to clients.
    options.ConfigureHttpsDefaults(https => https.ServerCertificate = serverCertificate);
});

builder.Configuration.AddJsonFile(path: "ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Client", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

builder.Services.AddOcelot().AddDelegatingHandler<HideDownstreamErrorsHandler>(global: true);
builder.Services.AddSingleton<IMessageInvokerPool, MtlsMessageInvokerPool>(); // Replaces Ocelot's default: adds mTLS to downstream calls.

// DI
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddIdentitySettings();
builder.Services.AddAuthRateLimiting();

var app = builder.Build();

// Ocelot copies downstream headers: strip the ones that reveal the stack, add the API security headers.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.XContentTypeOptions = "nosniff";               // No MIME sniffing of JSON responses.
        headers["Cross-Origin-Resource-Policy"] = "same-origin"; // Other sites can't embed API responses.
        headers.CacheControl = "no-store";                      // Responses may hold personal data; never cache.
        return Task.CompletedTask;
    });
    await next();
});

// Explicit, so they run after the header middleware above. Without these calls ASP.NET inserts them at the very
// start of the pipeline; the JWT handler then writes 401 responses before OnStarting is registered, and the request crashes.
app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // Tells browsers to use HTTPS only for this host.
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("Client");
app.UseRateLimiter();
app.UseMiddleware<ErrorHandlingMiddleware>();
await app.UseOcelot();
app.Run();
