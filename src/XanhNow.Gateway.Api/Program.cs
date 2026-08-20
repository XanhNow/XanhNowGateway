using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using System.Net;
using System.Reflection;
using XanhNow.Gateway.Api.AssetLinks;
using XanhNow.Gateway.Api.Options;
using XanhNow.Gateway.Api.Proxy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddSingleton<AssetLinksResponseFactory>();
builder.Services.AddSingleton<ReverseProxyHandler>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Parse("192.168.2.24"));
    options.KnownProxies.Add(IPAddress.Parse("192.168.2.64"));
});
builder.Services.AddHttpClient(ReverseProxyHandler.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    await next();
});

app.MapGet("/api/health/live", () => Results.Ok(new { status = "Healthy", service = "xanhnow-gateway" }));
app.MapGet("/api/health/ready", (IOptions<GatewayOptions> options) =>
{
    var routeCount = options.Value.Routes.Count;
    return Results.Ok(new { status = "Healthy", service = "xanhnow-gateway", routes = routeCount });
});

app.MapGet("/api/edge-probe", (HttpContext context) => Results.Ok(new
{
    service = "xanhnow-gateway",
    node = Environment.MachineName,
    source_sha = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    forwarded_for = context.Request.Headers["X-Forwarded-For"].ToString(),
    forwarded_proto = context.Request.Headers["X-Forwarded-Proto"].ToString(),
    path = context.Request.Path.Value
}));

app.MapGet("/health/live", () => Results.Redirect("/api/health/live", permanent: false));
app.MapGet("/health/ready", () => Results.Redirect("/api/health/ready", permanent: false));

app.MapGet("/.well-known/assetlinks.json", (IOptions<GatewayOptions> options, AssetLinksResponseFactory factory) =>
{
    var statements = factory.Create(options.Value.AndroidAssetLinks);
    return Results.Json(statements, contentType: "application/json");
});

app.MapFallback(async (HttpContext context, IOptions<GatewayOptions> options, ReverseProxyHandler proxy) =>
{
    if (IsDeniedDirectChildAppRoute(context.Request.Path, options.Value.DeniedDirectPrefixes))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "gateway.direct_child_app_route_forbidden",
            message = "Direct child application routes are forbidden. Requests must enter through XanhNow.Security orchestration."
        }, context.RequestAborted);
        return;
    }

    if (await proxy.TryProxyAsync(context))
    {
        return;
    }

    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await context.Response.WriteAsJsonAsync(new
    {
        error = "gateway.route_not_found",
        message = "No gateway route matched the request path."
    }, context.RequestAborted);
});

app.Run();

static bool IsDeniedDirectChildAppRoute(PathString path, IEnumerable<string> prefixes)
{
    return prefixes.Any(prefix => !string.IsNullOrWhiteSpace(prefix) && path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}

public partial class Program
{
}

