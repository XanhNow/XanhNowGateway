using Microsoft.Extensions.Options;
using XanhNow.Gateway.Api.Options;

namespace XanhNow.Gateway.Api.Proxy;

public sealed class ReverseProxyHandler
{
    public const string HttpClientName = "xanhnow-gateway-proxy";

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Host"
    };

    private static readonly HashSet<string> SpoofableInternalHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-XanhNow-UserId",
        "X-XanhNow-PhoneNumber",
        "X-XanhNow-Gateway-Key"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly ILogger<ReverseProxyHandler> _logger;

    public ReverseProxyHandler(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<GatewayOptions> options,
        ILogger<ReverseProxyHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> TryProxyAsync(HttpContext context)
    {
        var route = MatchRoute(context.Request.Path, _options.CurrentValue.Routes);
        if (route is null)
        {
            return false;
        }

        using var requestMessage = CreateProxyRequest(context, route);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage responseMessage;
        try
        {
            responseMessage = await client.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteGatewayErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "gateway.downstream_timeout", "Downstream service timed out.");
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Downstream route {RouteName} is unavailable.", route.Name);
            await WriteGatewayErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "gateway.downstream_unavailable", "Downstream service is unavailable.");
            return true;
        }

        using (responseMessage)
        {
            context.Response.StatusCode = (int)responseMessage.StatusCode;
            CopyHeaders(responseMessage, context.Response);

            await responseMessage.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        _logger.LogInformation("Proxied {Method} {Path} to {RouteName} with status {StatusCode}.", context.Request.Method, context.Request.Path, route.Name, context.Response.StatusCode);
        return true;
    }

    public static GatewayRouteOptions? MatchRoute(PathString path, IEnumerable<GatewayRouteOptions> routes)
    {
        return routes
            .Where(route => !string.IsNullOrWhiteSpace(route.Prefix) && path.StartsWithSegments(route.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(route => route.Prefix.Length)
            .FirstOrDefault();
    }

    public static Uri BuildTargetUri(HttpRequest request, GatewayRouteOptions route)
    {
        if (!request.Path.StartsWithSegments(route.Prefix, out var remaining))
        {
            throw new InvalidOperationException($"Request path does not match route prefix '{route.Prefix}'.");
        }

        var destination = route.DestinationBaseAddress.TrimEnd('/');
        var path = route.StripPrefix ? remaining.ToString() : request.Path.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        return new Uri($"{destination}{path}{request.QueryString}", UriKind.Absolute);
    }

    private static HttpRequestMessage CreateProxyRequest(HttpContext context, GatewayRouteOptions route)
    {
        var request = context.Request;
        var targetUri = BuildTargetUri(request, route);
        var requestMessage = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) || SpoofableInternalHeaders.Contains(header.Key))
            {
                continue;
            }

            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                requestMessage.Content ??= new StreamContent(request.Body);
                requestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        if (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
        {
            requestMessage.Content ??= new StreamContent(request.Body);
        }

        requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-Host", request.Host.Value);
        requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-Proto", request.Scheme);
        requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-Path", request.Path.Value ?? string.Empty);

        return requestMessage;
    }

    private static Task WriteGatewayErrorAsync(HttpContext context, int statusCode, string errorCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new
        {
            error = errorCode,
            message
        }, context.RequestAborted);
    }
    private static void CopyHeaders(HttpResponseMessage responseMessage, HttpResponse response)
    {
        foreach (var header in responseMessage.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        response.Headers.Remove("transfer-encoding");
    }
}


