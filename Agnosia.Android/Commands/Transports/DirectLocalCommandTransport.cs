#if AGNOSIA_ANDROID
using Android.Content;
#endif

namespace Agnosia.Android.Commands.Transports;

internal sealed class DirectLocalCommandTransport : IAndroidCommandTransport
{
    private readonly AndroidCommandHandlerExecutor _executor;

#if AGNOSIA_ANDROID
    private readonly Context _applicationContext;
    private readonly AndroidCommandExecutionContextFactory _contextFactory;

    public DirectLocalCommandTransport(
        AndroidCommandHandlerExecutor executor,
        Context applicationContext,
        AndroidCommandExecutionContextFactory contextFactory)
    {
        _executor = executor;
        _applicationContext = applicationContext;
        _contextFactory = contextFactory;
    }
#else
    public DirectLocalCommandTransport(AndroidCommandHandlerExecutor executor)
    {
        _executor = executor;
    }
#endif

    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.DirectLocal;

    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
#if AGNOSIA_ANDROID
        var context = _contextFactory.Create(
            _applicationContext,
            null,
            envelope,
            Kind,
            "application");
        return _executor.ExecuteAsync(envelope, context, cancellationToken);
#else
        return _executor.ExecuteAsync(
            envelope,
            AndroidCommandExecutionContext.ForTests(
                Kind,
                envelope.TargetProfile,
                AndroidCommandExecutionProfile.Unknown,
                "non-android"),
            cancellationToken);
#endif
    }
}
