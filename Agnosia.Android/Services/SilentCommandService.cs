#if AGNOSIA_ANDROID
using Android.App;
using Android.Content;
using Android.OS;

namespace Agnosia.Android.Services;

[Service(
    Name = "com.agnosia.app.SilentCommandService",
    Exported = true,
    Permission = "com.agnosia.app.permission.CROSS_PROFILE_COMMAND")]
public sealed class SilentCommandService : Service
{
    private SilentCommandBinder? _binder;

    public override IBinder OnBind(Intent? intent)
    {
        AgnosiaRuntime.Initialize(this);
        return _binder ??= new SilentCommandBinder(this);
    }

    internal sealed class SilentCommandBinder(SilentCommandService service) : Binder
    {
        public Task<AndroidCommandResultEnvelope> ExecuteAsync(
            AndroidCommandEnvelope envelope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contextFactory = ServiceRegistry.GetRequiredService<AndroidCommandExecutionContextFactory>();
            var executor = ServiceRegistry.GetRequiredService<AndroidCommandHandlerExecutor>();
            var context = contextFactory.Create(
                service,
                null,
                envelope,
                AndroidCommandTransportKind.SilentService,
                "service");

            return executor.ExecuteAsync(envelope, context, cancellationToken);
        }
    }
}
#endif
