#if AGNOSIA_ANDROID
using Agnosia.Android.Services;
using Android.Content;
using Android.OS;

namespace Agnosia.Android.Commands.Transports;

internal sealed class SilentParentProfileCommandTransport(Context applicationContext) : IAndroidCommandTransport
{
    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.SilentParentProfile;

    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (envelope.TargetProfile != AndroidCommandTargetProfile.Personal)
            return Unavailable(envelope, "targetProfile!=Personal");

        var context = applicationContext.ApplicationContext ?? applicationContext;
        if (!AgnosiaUtilities.IsProfileOwner(context))
            return Unavailable(envelope, "sourceProfile!=Work");

        var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(context);
        if (crossProfileApps is null)
            return Unavailable(envelope, "crossProfileApps=missing");

        if (!crossProfileApps.CanInteractAcrossProfiles())
            return Unavailable(
                envelope,
                "canInteractAcrossProfiles=false; requiresPermission=android.permission.INTERACT_ACROSS_PROFILES");

        var targetUser = crossProfileApps.TargetUserProfiles
            .OfType<UserHandle>()
            .FirstOrDefault();
        if (targetUser is null)
            return Unavailable(envelope, "targetUser=missing");

        if (string.IsNullOrWhiteSpace(context.PackageName))
            return Unavailable(envelope, "packageName=missing");

        var intent = new Intent();
        intent.SetComponent(new ComponentName(context.PackageName, SilentCommandService.ServiceName));
        return await SilentCommandMessengerClient.ExecuteAsync(
                envelope,
                Kind,
                connection => context.BindServiceAsUser(
                    intent,
                    connection,
                    (int)Bind.AutoCreate,
                    targetUser),
                context.UnbindService,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private AndroidCommandResultEnvelope Unavailable(AndroidCommandEnvelope envelope, string diagnostics)
    {
        return AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            Kind,
            "Silent parent-profile command transport is not available on this Android profile topology.",
            "silent_parent_transport_unavailable",
            TimeSpan.Zero,
            $"capability=conditional; api=Context.BindServiceAsUser; {diagnostics}; fallback=none");
    }
}
#else
namespace Agnosia.Android.Commands.Transports;

internal sealed class SilentParentProfileCommandTransport : IAndroidCommandTransport
{
    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.SilentParentProfile;

    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            Kind,
            "Silent parent-profile command transport can execute only on the Android target.",
            "android_target_required",
            TimeSpan.Zero,
            "target=net10.0"));
    }
}
#endif
