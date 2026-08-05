using Agnosia.Android.Activities;
using Xunit;

namespace Agnosia.Unit.Android.Activities;

public sealed class PackageRemovalVisibilityTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldRollback_MatchesOriginalHiddenStateAndOutcome(
        bool restoreHiddenState,
        bool uninstallSucceeded,
        bool expected)
    {
        Assert.Equal(
            expected,
            PackageRemovalVisibility.ShouldRollback(restoreHiddenState, uninstallSucceeded));
    }
}
