using Agnosia.Models;

namespace Agnosia.Android.Packages;

internal sealed record PackageInventoryMetadata(
    PackageIdentity? Identity, AppPermissionRiskAnalysis Risk, bool RiskAvailable)
{
    public static PackageInventoryMetadata Read(bool analyzeRisk,
        Func<PackageInventoryMetadata?> readRisk, Func<PackageIdentity?> readIdentity)
    {
        if (analyzeRisk && readRisk() is { } metadata) return metadata;
        // Optional details must not remove a package already enumerated by Android.
        return new(readIdentity(), AppPermissionRiskAnalysis.Safe, false);
    }
}
