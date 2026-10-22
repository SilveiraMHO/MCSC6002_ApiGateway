using ApiGateway;
using ApiGateway.Middlewares;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

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

// DI
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddIdentitySettings();

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

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("Client");
app.UseMiddleware<ErrorHandlingMiddleware>();
await app.UseOcelot();
app.Run();
