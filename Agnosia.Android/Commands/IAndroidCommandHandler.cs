namespace Agnosia.Android.Commands;

internal interface IAndroidCommandHandler
{
    AndroidCommandKind Kind { get; }

#if AGNOSIA_ANDROID
    Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken);
#endif
}
