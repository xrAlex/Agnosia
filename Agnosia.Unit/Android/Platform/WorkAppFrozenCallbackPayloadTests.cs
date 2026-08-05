using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.Android.Platform;

public sealed class WorkAppFrozenCallbackPayloadTests
{
    [Fact]
    public void Create_binds_package_and_launch_identity()
    {
        const string expected = "AGNOSIA_WORK_APP_FROZEN_CALLBACK_2\ncom.example.app\nlaunch-42";

        var actual = WorkAppFrozenCallbackPayload.Create("com.example.app", "launch-42");

        Assert.Equal(expected, actual);
        Assert.NotEqual(actual, WorkAppFrozenCallbackPayload.Create("com.example.other", "launch-42"));
        Assert.NotEqual(actual, WorkAppFrozenCallbackPayload.Create("com.example.app", "launch-43"));
    }

    [Fact]
    public void CreateLegacy_preserves_v1_payload_for_migration()
    {
        const string expected = "AGNOSIA_WORK_APP_FROZEN_CALLBACK_1\ncom.example.legacy";

        var actual = WorkAppFrozenCallbackPayload.CreateLegacy("com.example.legacy");

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("", "launch")]
    [InlineData("com.example", "")]
    public void Create_rejects_blank_identity(string packageName, string launchId)
    {
        Assert.Throws<ArgumentException>(() => WorkAppFrozenCallbackPayload.Create(packageName, launchId));
    }
}
