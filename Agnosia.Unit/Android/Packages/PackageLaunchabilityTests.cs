using Agnosia.Android.Packages;
using Xunit;

namespace Agnosia.Unit.Android.Packages;

public sealed class PackageLaunchabilityTests
{
    [Fact]
    public void CanLaunch_ReturnsFalse_WhenPackageHasNoFrontDoorActivity()
    {
        var query = new TestPackageLaunchQuery();

        Assert.False(PackageLaunchability.CanLaunch(packageAvailable: true, query));
    }

    [Fact]
    public void CanLaunch_ReturnsTrue_WhenPackageHasDirectLaunchIntent()
    {
        var query = new TestPackageLaunchQuery { DirectLaunchIntentFound = true };

        Assert.True(PackageLaunchability.CanLaunch(packageAvailable: true, query));
    }

    [Fact]
    public void CanLaunch_ReturnsTrue_WhenHiddenPackageHasInfoActivity()
    {
        var query = new TestPackageLaunchQuery { InfoActivityFound = true };

        Assert.True(PackageLaunchability.CanLaunch(packageAvailable: true, query));
    }

    [Fact]
    public void CanLaunch_ReturnsTrue_WhenHiddenPackageHasLauncherActivity()
    {
        var query = new TestPackageLaunchQuery { LauncherActivityFound = true };

        Assert.True(PackageLaunchability.CanLaunch(packageAvailable: true, query));
    }

    [Fact]
    public void CanLaunch_ReturnsFalse_WhenOnlyUninstalledPackageMetadataMatches()
    {
        var query = new TestPackageLaunchQuery { LauncherActivityFound = true };

        Assert.False(PackageLaunchability.CanLaunch(packageAvailable: false, query));
    }

    private sealed class TestPackageLaunchQuery : IPackageLaunchQuery
    {
        public bool DirectLaunchIntentFound { get; init; }

        public bool InfoActivityFound { get; init; }

        public bool LauncherActivityFound { get; init; }

        public bool HasDirectLaunchIntent()
        {
            return DirectLaunchIntentFound;
        }

        public bool HasInfoActivity()
        {
            return InfoActivityFound;
        }

        public bool HasLauncherActivity()
        {
            return LauncherActivityFound;
        }
    }
}
