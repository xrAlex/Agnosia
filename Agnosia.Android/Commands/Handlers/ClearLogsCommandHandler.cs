namespace Agnosia.Android.Commands.Handlers;

internal sealed class ClearLogsCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.ClearLogs;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope, AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AndroidAppLogArchive.Clear(context.Context, throwOnFailure: true);
        return Task.FromResult(AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId, envelope.Kind, context.Transport, null,
            "Журнал очищен.", TimeSpan.Zero, "archiveCleared=true"));
    }
#endif
}
