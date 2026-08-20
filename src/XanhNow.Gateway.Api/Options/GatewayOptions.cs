using System.Text.Json.Serialization;

namespace XanhNow.Gateway.Api.Options;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public string PublicBaseUrl { get; set; } = "https://api.ioxy.site";

    public List<GatewayRouteOptions> Routes { get; set; } = [];

    public List<string> DeniedDirectPrefixes { get; set; } = [];

    public List<AndroidAssetLinkOptions> AndroidAssetLinks { get; set; } = [];
}

public sealed class GatewayRouteOptions
{
    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string DestinationBaseAddress { get; set; } = string.Empty;

    public bool StripPrefix { get; set; } = true;
}

public sealed class AndroidAssetLinkOptions
{
    public string PackageName { get; set; } = string.Empty;

    public List<string> Sha256CertFingerprints { get; set; } = [];

    public List<string> Relations { get; set; } =
    [
        "delegate_permission/common.handle_all_urls",
        "delegate_permission/common.get_login_creds"
    ];
}

public sealed record AndroidAssetLinkStatement(
    [property: JsonPropertyName("relation")] string[] Relation,
    [property: JsonPropertyName("target")] AndroidAssetLinkTarget Target);

public sealed record AndroidAssetLinkTarget(
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("package_name")] string PackageName,
    [property: JsonPropertyName("sha256_cert_fingerprints")] string[] Sha256CertFingerprints);
