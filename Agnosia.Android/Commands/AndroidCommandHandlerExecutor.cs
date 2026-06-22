namespace Agnosia.Android.Commands;

internal sealed class AndroidCommandHandlerExecutor
{
    private readonly IReadOnlyDictionary<AndroidCommandKind, IAndroidCommandHandler> _handlers;

    public AndroidCommandHandlerExecutor(IEnumerable<IAndroidCommandHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.Kind);
    }

    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ProfilesMatch(envelope.TargetProfile, context.ActualProfile))
            return Task.FromResult(AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                "Command execution profile does not match requested target profile.",
                "profile_mismatch",
                TimeSpan.Zero,
                $"requested={envelope.TargetProfile}; actual={context.ActualProfile}; contextSource={context.ContextSource}"));

        if (!_handlers.TryGetValue(envelope.Kind, out var handler))
            return Task.FromResult(AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                $"No command handler is registered for {envelope.Kind}.",
                "handler_missing",
                TimeSpan.Zero,
                $"requested={envelope.TargetProfile}; actual={context.ActualProfile}; contextSource={context.ContextSource}"));

#if AGNOSIA_ANDROID
        return ExecuteHandlerAsync(handler, envelope, context, cancellationToken);
#else
        return Task.FromResult(AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            "Android command handlers can execute only on the Android target.",
            "android_target_required",
            TimeSpan.Zero,
            $"handler={handler.GetType().Name}; contextSource={context.ContextSource}"));
#endif
    }

#if AGNOSIA_ANDROID
    private static async Task<AndroidCommandResultEnvelope> ExecuteHandlerAsync(
        IAndroidCommandHandler handler,
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.ExecuteAsync(envelope, context, cancellationToken).ConfigureAwait(false);
        return AppendContextDiagnostics(result, envelope, context);
    }
#endif

    private static AndroidCommandResultEnvelope AppendContextDiagnostics(
        AndroidCommandResultEnvelope result,
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context)
    {
        var contextDiagnostics =
            $"requested={envelope.TargetProfile}; actual={context.ActualProfile}; contextSource={context.ContextSource}";
        if (string.IsNullOrWhiteSpace(result.Diagnostics))
            return result with { Diagnostics = contextDiagnostics };

        return result with { Diagnostics = $"{result.Diagnostics}; {contextDiagnostics}" };
    }

    private static bool ProfilesMatch(
        AndroidCommandTargetProfile requested,
        AndroidCommandExecutionProfile actual)
    {
        return requested switch
        {
            AndroidCommandTargetProfile.Personal => actual == AndroidCommandExecutionProfile.Personal,
            AndroidCommandTargetProfile.Work => actual == AndroidCommandExecutionProfile.Work,
            _ => false
        };
    }
}
