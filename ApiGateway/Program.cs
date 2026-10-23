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

// Ocelot copies downstream response headers; strip ones that reveal the internal stack.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.Remove("Server");
        context.Response.Headers.Remove("X-Powered-By");
        return Task.CompletedTask;
    });
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // Tells browsers to use HTTPS only for this host.
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("Client");
app.UseRateLimiter();
app.UseMiddleware<ErrorHandlingMiddleware>();
await app.UseOcelot();
app.Run();
