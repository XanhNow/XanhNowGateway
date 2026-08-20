using System.Text.Json;
using Microsoft.AspNetCore.Http;
using XanhNow.Gateway.Api.AssetLinks;
using XanhNow.Gateway.Api.Options;
using XanhNow.Gateway.Api.Proxy;

namespace XanhNow.Gateway.Api.Tests;

public sealed class AssetLinksResponseFactoryTests
{
    [Fact]
    public void Create_returns_android_digital_asset_links_contract()
    {
        var factory = new AssetLinksResponseFactory();

        var result = factory.Create(
        [
            new AndroidAssetLinkOptions
            {
                PackageName = "com.xanhnow.flutter",
                Sha256CertFingerprints =
                [
                    "6E:95:1A:D8:76:CB:C3:83:37:C4:6E:AC:7C:05:EB:46:D5:72:C1:AA:70:AD:F7:F1:E4:79:3C:90:79:01:6A:DE"
                ]
            }
        ]).Single();

        Assert.Contains("delegate_permission/common.get_login_creds", result.Relation);
        Assert.Contains("delegate_permission/common.handle_all_urls", result.Relation);
        Assert.Equal("android_app", result.Target.Namespace);
        Assert.Equal("com.xanhnow.flutter", result.Target.PackageName);
        Assert.Contains("6E:95:1A:D8:76:CB:C3:83:37:C4:6E:AC:7C:05:EB:46:D5:72:C1:AA:70:AD:F7:F1:E4:79:3C:90:79:01:6A:DE", result.Target.Sha256CertFingerprints);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"package_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sha256_cert_fingerprints\"", json, StringComparison.Ordinal);
    }
}

public sealed class ReverseProxyHandlerTests
{
    [Fact]
    public void MatchRoute_prefers_longest_prefix()
    {
        var route = ReverseProxyHandler.MatchRoute(
            "/api/security/api/v1/auth/register",
            [
                new GatewayRouteOptions { Name = "api-root", Prefix = "/api", DestinationBaseAddress = "http://localhost:1" },
                new GatewayRouteOptions { Name = "security", Prefix = "/api/security", DestinationBaseAddress = "http://localhost:5068" }
            ]);

        Assert.NotNull(route);
        Assert.Equal("security", route.Name);
    }

    [Fact]
    public void Gateway_routes_only_public_security_surface()
    {
        var options = new GatewayOptions
        {
            DeniedDirectPrefixes = ["/api/customer", "/api/object-storage", "/customer", "/object-storage"],
            Routes =
            [
                new GatewayRouteOptions { Name = "security", Prefix = "/api/security", DestinationBaseAddress = "http://localhost:5068" }
            ]
        };

        Assert.Single(options.Routes);
        Assert.Equal("security", options.Routes[0].Name);
        Assert.Contains("/api/customer", options.DeniedDirectPrefixes);
        Assert.Contains("/api/object-storage", options.DeniedDirectPrefixes);
    }

    [Fact]
    public void BuildTargetUri_strips_gateway_api_security_prefix_and_preserves_query_string()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.ioxy.site");
        context.Request.Path = "/api/security/api/v1/auth/register";
        context.Request.QueryString = new QueryString("?draft=true");

        var uri = ReverseProxyHandler.BuildTargetUri(
            context.Request,
            new GatewayRouteOptions
            {
                Name = "security",
                Prefix = "/api/security",
                DestinationBaseAddress = "http://localhost:5068",
                StripPrefix = true
            });

        Assert.Equal("http://localhost:5068/api/v1/auth/register?draft=true", uri.ToString());
    }
}
