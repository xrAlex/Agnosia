using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class WorkAppFrozenCommandPayloadTests
{
    [Fact]
    public void RoundTrip_preserves_exact_callback_identity()
    {
        var expected = WorkAppFrozenCommandPayload.Create(
            "com.example.app",
            "launch-42",
            "session_hide:target_inactive:com.example.app");

        var parsed = WorkAppFrozenCommandPayload.TryDeserialize(expected.Serialize(), out var actual);

        Assert.True(parsed);
        Assert.Equal("com.example.app", actual.PackageName);
        Assert.Equal("launch-42", actual.LaunchId);
        Assert.Equal("session_hide:target_inactive:com.example.app", actual.Trigger);
    }

    [Theory]
    [InlineData("", "launch-42", "trigger")]
    [InlineData("com.example.app", "", "trigger")]
    [InlineData("com.example.app", "launch-42", "")]
    [InlineData(" ", "launch-42", "trigger")]
    public void Create_rejects_blank_required_identity(string packageName, string launchId, string trigger)
    {
        Assert.Throws<ArgumentException>(() => WorkAppFrozenCommandPayload.Create(packageName, launchId, trigger));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"PackageName\":\"com.example.app\",\"LaunchId\":\"\",\"Trigger\":\"trigger\"}")]
    public void TryDeserialize_rejects_missing_malformed_or_incomplete_payload(string? raw)
    {
        var parsed = WorkAppFrozenCommandPayload.TryDeserialize(raw, out _);

        Assert.False(parsed);
    }
}
