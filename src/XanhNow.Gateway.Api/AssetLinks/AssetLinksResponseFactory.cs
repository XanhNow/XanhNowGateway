using XanhNow.Gateway.Api.Options;

namespace XanhNow.Gateway.Api.AssetLinks;

public sealed class AssetLinksResponseFactory
{
    public IReadOnlyCollection<AndroidAssetLinkStatement> Create(IEnumerable<AndroidAssetLinkOptions> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        return apps
            .Where(app => !string.IsNullOrWhiteSpace(app.PackageName))
            .Select(app => new AndroidAssetLinkStatement(
                app.Relations.Where(relation => !string.IsNullOrWhiteSpace(relation)).Distinct(StringComparer.Ordinal).ToArray(),
                new AndroidAssetLinkTarget(
                    "android_app",
                    app.PackageName,
                    app.Sha256CertFingerprints
                        .Where(fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray())))
            .Where(statement => statement.Relation.Length > 0 && statement.Target.Sha256CertFingerprints.Length > 0)
            .ToArray();
    }
}
