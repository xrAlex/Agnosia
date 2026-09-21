using Agnosia.Android.Packages;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Packages;

public sealed class PackageInventoryMetadataTests
{
    [Fact]
    public void Failed_permission_query_keeps_identity_and_marks_risk_unavailable()
    {
        var metadata = PackageInventoryMetadata.Read(true, () => null, () => new PackageIdentity(42));
        Assert.Equal(new PackageIdentity(42), metadata.Identity);
        Assert.False(metadata.RiskAvailable);
    }

    [Fact]
    public void Missing_package_details_still_return_metadata_for_inventory_row()
    {
        var metadata = PackageInventoryMetadata.Read(true, () => null, () => null);
        Assert.NotNull(metadata);
        Assert.Null(metadata.Identity);
        Assert.False(metadata.RiskAvailable);
    }

    [Fact]
    public void Successful_analysis_is_retained_without_second_package_query()
    {
        var expected = new PackageInventoryMetadata(new PackageIdentity(42), AppPermissionRiskAnalysis.Safe, true);
        var metadata = PackageInventoryMetadata.Read(true, () => expected,
            () => throw new InvalidOperationException("Unnecessary package query"));
        Assert.Same(expected, metadata);
    }

    [Fact]
    public void Disabled_risk_engine_does_not_read_permissions()
    {
        var metadata = PackageInventoryMetadata.Read(false,
            () => throw new InvalidOperationException("Risk engine is disabled"), () => new PackageIdentity(42));
        Assert.Equal(new PackageIdentity(42), metadata.Identity);
        Assert.False(metadata.RiskAvailable);
    }
}
