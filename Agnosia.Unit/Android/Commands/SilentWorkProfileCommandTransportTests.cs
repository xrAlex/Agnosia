using Agnosia.Android.Commands;
using Agnosia.Android.Commands.Transports;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class SilentWorkProfileCommandTransportTests
{
    [Fact]
    public async Task ExecuteAsync_OnNet10Target_ReturnsAndroidTargetRequiredFailure()
    {
        var transport = new SilentWorkProfileCommandTransport();
        var envelope = new AndroidCommandEnvelope(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            AndroidCommandKind.QueryApps,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh,
            TimeSpan.FromSeconds(30),
            null);

        var result = await transport.ExecuteAsync(envelope, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.SilentWorkProfile, result.Transport);
        Assert.Equal("android_target_required", result.ErrorCode);
        Assert.Contains("target=net10.0", result.Diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void Android_implementation_binds_work_profile_silent_service_as_user()
    {
        var source = File.ReadAllText(RepositoryPaths.Get(
            "Agnosia.Android",
            "Commands",
            "Transports",
            "SilentWorkProfileCommandTransport.cs"));

        Assert.Contains("BindServiceAsUser", source, StringComparison.Ordinal);
        Assert.Contains("TargetUserProfiles", source, StringComparison.Ordinal);
        Assert.Contains("SilentCommandMessengerClient.ExecuteAsync", source, StringComparison.Ordinal);
        Assert.Contains("CanInteractAcrossProfiles", source, StringComparison.Ordinal);
        Assert.Contains("(int)Bind.AutoCreate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("requires_api_34", source, StringComparison.Ordinal);
        Assert.Contains("SilentCommandService.ServiceName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Silent_command_messenger_client_times_out_and_cleans_up_failed_bind_attempts()
    {
        var source = File.ReadAllText(RepositoryPaths.Get(
            "Agnosia.Android",
            "Commands",
            "Transports",
            "SilentCommandMessengerClient.cs"));

        Assert.Contains("GetReplyTimeout", source, StringComparison.Ordinal);
        Assert.Contains("silent_service_reply_timeout", source, StringComparison.Ordinal);
        Assert.Contains("bindAttempted", source, StringComparison.Ordinal);
        Assert.Contains("if (bindAttempted) Unbind", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Silent_command_service_frames_handler_exceptions_as_result_envelopes()
    {
        var messengerClientSource = File.ReadAllText(RepositoryPaths.Get(
            "Agnosia.Android",
            "Commands",
            "Transports",
            "SilentCommandMessengerClient.cs"));
        var serviceSource = File.ReadAllText(RepositoryPaths.Get(
            "Agnosia.Android",
            "Services",
            "SilentCommandService.cs"));

        Assert.Contains("command_handler_exception", messengerClientSource, StringComparison.Ordinal);
        Assert.Contains("await executeAsync", messengerClientSource, StringComparison.Ordinal);
        Assert.Contains("command_service_exception", serviceSource, StringComparison.Ordinal);
        Assert.Contains("TrySendReply", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Silent_command_service_snapshots_reply_messenger_before_async_execution()
    {
        var serviceSource = File.ReadAllText(RepositoryPaths.Get(
            "Agnosia.Android",
            "Services",
            "SilentCommandService.cs"));

        Assert.Contains("var commandJson = msg.Data?.GetString", serviceSource, StringComparison.Ordinal);
        Assert.Contains("var replyTo = msg.ReplyTo", serviceSource, StringComparison.Ordinal);
        Assert.Contains("HandleCommandAsync(service, commandJson, replyTo)", serviceSource, StringComparison.Ordinal);
        Assert.Contains("Messenger? replyTo", serviceSource, StringComparison.Ordinal);
        Assert.Contains("replyTo?.Send", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleCommandAsync(service, msg)", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("request.ReplyTo?.Send", serviceSource, StringComparison.Ordinal);
    }
}
