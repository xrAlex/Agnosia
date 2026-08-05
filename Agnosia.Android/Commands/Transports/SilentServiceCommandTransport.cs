#if AGNOSIA_ANDROID
using Agnosia.Android.Services;
using Android.Content;

namespace Agnosia.Android.Commands.Transports;

internal sealed class SilentServiceCommandTransport(Context applicationContext) : IAndroidCommandTransport
{
    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.SilentService;

    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.TargetProfile != AndroidCommandTargetProfile.Personal)
        {
            return AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                Kind,
                "Local silent service transport cannot execute commands for another profile.",
                "silent_service_wrong_profile",
                TimeSpan.Zero,
                $"requested={envelope.TargetProfile}; supported=Personal");
        }

        var bindContext = applicationContext.ApplicationContext ?? applicationContext;
        var intent = new Intent(bindContext, typeof(SilentCommandService));
        return await SilentCommandMessengerClient.ExecuteAsync(
                envelope,
                Kind,
                connection => bindContext.BindService(intent, connection, Bind.AutoCreate),
                bindContext.UnbindService,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
#endif
