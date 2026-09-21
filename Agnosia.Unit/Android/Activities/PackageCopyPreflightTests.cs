using Agnosia.Android.Activities;
using Xunit;

namespace Agnosia.Unit.Android.Activities;

public sealed class PackageCopyPreflightTests
{
    [Theory]
    [InlineData(true, true, true, 2L, 2L, true)]
    [InlineData(true, true, true, 2L, 3L, true)]
    [InlineData(true, true, true, 3L, 2L, false)]
    [InlineData(true, true, true, 0L, 0L, true)]
    [InlineData(true, true, true, null, 2L, false)]
    [InlineData(true, true, true, 2L, null, false)]
    [InlineData(true, true, true, -1L, 2L, false)]
    [InlineData(true, true, true, 2L, -1L, false)]
    [InlineData(false, true, true, 2L, 2L, false)]
    [InlineData(true, false, true, 2L, 2L, false)]
    [InlineData(true, true, false, 2L, 2L, false)]
    [InlineData(true, true, null, 2L, 2L, false)]
    public void Existing_work_copy_is_reused_only_when_it_is_not_older(
        bool requested, bool owner, bool? installed, long? sourceVersionCode,
        long? workVersionCode, bool expected)
    {
        Assert.Equal(expected, PackageCopyPreflight.CanReuseExistingPackage(
            requested, owner, installed, sourceVersionCode, workVersionCode));
    }
}
