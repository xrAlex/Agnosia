using System.Text.Json;
using Agnosia.Android.Vpn;
using Xunit;

namespace Agnosia.Unit.Android.Vpn;

public sealed class VpnRestoreOwnershipCodecTests
{
    [Fact]
    public void RoundTrip_preserves_active_pending_and_version()
    {
        var expected = new VpnRestoreOwnershipState(
            true,
            new VpnRestoreOwner("launch-a", "com.example.a"),
            new VpnRestoreOwner("launch-b", "com.example.b"));

        var json = VpnRestoreOwnershipCodec.Serialize(expected);
        var parsed = VpnRestoreOwnershipCodec.TryDeserialize(json, out var actual);

        Assert.True(parsed);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Serialize_writes_current_version()
    {
        var json = VpnRestoreOwnershipCodec.Serialize(VpnRestoreOwnershipState.Empty);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            VpnRestoreOwnershipState.CurrentVersion,
            document.RootElement.GetProperty("Version").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"Version\":99,\"RestoreRequired\":true,\"ActiveOwner\":null,\"PendingOwner\":null}")]
    [InlineData("{\"Version\":1,\"RestoreRequired\":true,\"ActiveOwner\":{\"LaunchId\":\"\",\"PackageName\":\"com.example\"},\"PendingOwner\":null}")]
    [InlineData("{\"Version\":1,\"RestoreRequired\":true,\"ActiveOwner\":{\"LaunchId\":\"launch\",\"PackageName\":\"\"},\"PendingOwner\":null}")]
    public void TryDeserialize_rejects_missing_malformed_unknown_or_invalid_payload(string? raw)
    {
        var parsed = VpnRestoreOwnershipCodec.TryDeserialize(raw, out var state);

        Assert.False(parsed);
        Assert.Equal(VpnRestoreOwnershipState.Empty, state);
    }

    [Fact]
    public void RoundTrip_preserves_legacy_callback_marker()
    {
        var json = VpnRestoreOwnershipCodec.Serialize(VpnRestoreOwnershipState.Legacy);

        var parsed = VpnRestoreOwnershipCodec.TryDeserialize(json, out var actual);

        Assert.True(parsed);
        Assert.True(actual.RestoreRequired);
        Assert.True(actual.AcceptLegacyCallback);
        Assert.Null(actual.ActiveOwner);
        Assert.Null(actual.PendingOwner);
    }
}
