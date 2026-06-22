using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandHandlerExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsMissingHandlerFailure_WhenHandlerIsNotRegistered()
    {
        var executor = new AndroidCommandHandlerExecutor([]);
        var envelope = CreateEnvelope(AndroidCommandKind.QueryLogs, AndroidCommandTargetProfile.Personal);

        var result = await executor.ExecuteAsync(envelope, TestExecutionContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("handler_missing", result.ErrorCode);
        Assert.Equal(AndroidCommandTransportKind.DirectLocal, result.Transport);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsProfileMismatchFailure_WhenActualProfileDoesNotMatchTarget()
    {
        var executor = new AndroidCommandHandlerExecutor([new TestHandler(AndroidCommandKind.QueryLogs)]);
        var envelope = CreateEnvelope(AndroidCommandKind.QueryLogs, AndroidCommandTargetProfile.Work);
        var context = TestExecutionContext(actualProfile: AndroidCommandExecutionProfile.Personal);

        var result = await executor.ExecuteAsync(envelope, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("profile_mismatch", result.ErrorCode);
        Assert.Contains("requested=Work", result.Diagnostics, StringComparison.Ordinal);
        Assert.Contains("actual=Personal", result.Diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAndroidTargetFailure_WithContextDiagnostics()
    {
        var executor = new AndroidCommandHandlerExecutor([new TestHandler(AndroidCommandKind.QueryLogs)]);
        var envelope = CreateEnvelope(AndroidCommandKind.QueryLogs, AndroidCommandTargetProfile.Personal);

        var result = await executor.ExecuteAsync(envelope, TestExecutionContext(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("android_target_required", result.ErrorCode);
        Assert.Contains("contextSource=unit-test", result.Diagnostics, StringComparison.Ordinal);
    }

    private static AndroidCommandEnvelope CreateEnvelope(
        AndroidCommandKind kind,
        AndroidCommandTargetProfile targetProfile)
    {
        return new AndroidCommandEnvelope(
            Guid.NewGuid(),
            kind,
            targetProfile,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh,
            TimeSpan.FromSeconds(30),
            null);
    }

    private static AndroidCommandExecutionContext TestExecutionContext(
        AndroidCommandExecutionProfile actualProfile = AndroidCommandExecutionProfile.Personal)
    {
        return AndroidCommandExecutionContext.ForTests(
            AndroidCommandTransportKind.DirectLocal,
            AndroidCommandTargetProfile.Personal,
            actualProfile,
            "unit-test");
    }

    private sealed class TestHandler(AndroidCommandKind kind) : IAndroidCommandHandler
    {
        public AndroidCommandKind Kind => kind;
    }
}
