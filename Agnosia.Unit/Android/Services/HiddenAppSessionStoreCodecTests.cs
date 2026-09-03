using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Services;
using Xunit;

namespace Agnosia.Unit.Android.Services;

public sealed class HiddenAppSessionStoreCodecTests
{
    [Fact]
    public void RoundTrip_preserves_active_and_pending_retry_metadata()
    {
        var active = CreateSession("session-active", "com.example.active", 41) with
        {
            ParentCallbackLaunchId = "launch-active"
        };
        var pendingSession = CreateSession("session-pending", "com.example.pending", 42);
        var expectedPending = new HiddenAppPendingHideState(
            pendingSession,
            "task_removed",
            3,
            1_800_000_004_000);
        var expected = new HiddenAppSessionStoreState(active, [expectedPending]);

        var json = HiddenAppSessionStoreCodec.Serialize(expected);
        var parsed = HiddenAppSessionStoreCodec.TryDeserialize(json, out var actual);

        Assert.True(parsed);
        AssertSessionEqual(active, Assert.IsType<HiddenAppSessionState>(actual.ActiveSession));
        var actualPending = Assert.Single(actual.PendingHides);
        AssertSessionEqual(expectedPending.Session, actualPending.Session);
        Assert.Equal(expectedPending.Reason, actualPending.Reason);
        Assert.Equal(expectedPending.FailedAttempts, actualPending.FailedAttempts);
        Assert.Equal(expectedPending.NextAttemptAtUnixTimeMilliseconds, actualPending.NextAttemptAtUnixTimeMilliseconds);
        Assert.Equal(HiddenAppSessionStoreState.CurrentVersion, actual.Version);
    }

    [Fact]
    public void Serialize_writes_current_version()
    {
        var json = HiddenAppSessionStoreCodec.Serialize(HiddenAppSessionStoreState.Empty);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            HiddenAppSessionStoreState.CurrentVersion,
            document.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void Legacy_session_is_migrated_as_active_with_stable_identity()
    {
        const string legacy = """
                              {
                                "PackageName": "com.example.legacy",
                                "DisplayName": "Legacy",
                                "TaskId": 42,
                                "StartedAtUnixTimeMilliseconds": 1234,
                                "LaunchResult": null
                              }
                              """;

        var firstRead = HiddenAppSessionStoreCodec.TryDeserialize(legacy, out var first);
        var secondRead = HiddenAppSessionStoreCodec.TryDeserialize(legacy, out var second);

        Assert.True(firstRead);
        Assert.True(secondRead);
        Assert.Equal("com.example.legacy", first.ActiveSession?.PackageName);
        Assert.Equal("Legacy", first.ActiveSession?.DisplayName);
        Assert.Equal(first.ActiveSession?.SessionId, second.ActiveSession?.SessionId);
        Assert.False(string.IsNullOrWhiteSpace(first.ActiveSession?.SessionId));
        Assert.Empty(first.PendingHides);
    }

    [Fact]
    public void Version_one_store_is_migrated()
    {
        const string versionOne = """
                                  {
                                    "ActiveSession": null,
                                    "PendingHides": [],
                                    "Version": 1
                                  }
                                  """;

        var parsed = HiddenAppSessionStoreCodec.TryDeserialize(versionOne, out var state);

        Assert.True(parsed);
        Assert.Equal(HiddenAppSessionStoreState.CurrentVersion, state.Version);
        Assert.Empty(state.PendingHides);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"Version\":99,\"ActiveSession\":null,\"PendingHides\":[]}")]
    public void TryDeserialize_rejects_missing_malformed_or_unknown_payload(string? raw)
    {
        var parsed = HiddenAppSessionStoreCodec.TryDeserialize(raw, out var state);

        Assert.False(parsed);
        Assert.True(state.IsEmpty);
    }

    private static HiddenAppSessionState CreateSession(string sessionId, string packageName, int taskId)
    {
        return new HiddenAppSessionState(
            sessionId,
            packageName,
            packageName,
            taskId,
            1_800_000_000_000,
            AndroidAppLaunchResult.CommandReceived(packageName, packageName));
    }

    private static void AssertSessionEqual(HiddenAppSessionState expected, HiddenAppSessionState actual)
    {
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.PackageName, actual.PackageName);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.TaskId, actual.TaskId);
        Assert.Equal(expected.StartedAtUnixTimeMilliseconds, actual.StartedAtUnixTimeMilliseconds);
        Assert.Equal(expected.ParentCallbackLaunchId, actual.ParentCallbackLaunchId);
        Assert.Equal(expected.LaunchResult?.Stage, actual.LaunchResult?.Stage);
        Assert.Equal(expected.LaunchResult?.Message, actual.LaunchResult?.Message);
        Assert.Equal(expected.LaunchResult?.Events, actual.LaunchResult?.Events);
    }
}
