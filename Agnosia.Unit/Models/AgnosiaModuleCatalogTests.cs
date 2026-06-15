using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Models;

public sealed class AgnosiaModuleCatalogTests
{
    [Theory]
    [InlineData(AgnosiaModuleKind.FileShuttle)]
    [InlineData(AgnosiaModuleKind.Lockdown)]
    [InlineData(AgnosiaModuleKind.VpnGuard)]
    [InlineData(AgnosiaModuleKind.RiskEngine)]
    public void Unavailable_snapshot_uses_catalog_metadata(AgnosiaModuleKind kind)
    {
        var metadata = AgnosiaModuleCatalog.Get(kind);
        var snapshot = AgnosiaModuleSnapshot.Unavailable(kind);

        Assert.Equal(metadata.Title, snapshot.Title);
        Assert.Equal(metadata.ShortDescription, snapshot.ShortDescription);
        Assert.Equal(metadata.FullDescription, snapshot.FullDescription);
    }
}
