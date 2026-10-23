using ApiGateway;
using ApiGateway.Middlewares;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Requester;

var builder = WebApplication.CreateBuilder(args);

// Do not advertise the web server (Server: Kestrel) to clients.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

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

builder.Services.AddOcelot();
builder.Services.AddSingleton<IMessageInvokerPool, MtlsMessageInvokerPool>(); // Replaces Ocelot's default: adds mTLS to downstream calls.

// DI
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddIdentitySettings();
builder.Services.AddAuthRateLimiting();

var app = builder.Build();

// Response headers for every gateway response. Ocelot copies downstream headers, so strip the ones that
// reveal the internal stack, then add the API security headers (ZAP findings).
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
