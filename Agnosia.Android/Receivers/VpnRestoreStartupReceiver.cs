using Agnosia.Android.Activities;
using Agnosia.Android.Infrastructure;
using Agnosia.Android.Vpn;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(
    Name = "com.agnosia.app.VpnRestoreStartupReceiver",
    Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class VpnRestoreStartupReceiver : BroadcastReceiver
{
    private const string LogTag = "AgnosiaVpnRecoveryStartup";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !string.Equals(intent?.Action, Intent.ActionBootCompleted, StringComparison.Ordinal))
            return;

        try
        {
            AgnosiaRuntime.Initialize(context);
            if (AgnosiaUtilities.IsProfileOwner(context)) return;

            var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
            if (!VpnRestoreOwnershipCodec.TryDeserialize(
                    storage.GetString(StorageKeys.VpnRestoreOwnershipState),
                    out var state)
                || !state.RestoreRequired)
                return;

            var owner = state.ActiveOwner ?? state.PendingOwner;
            VpnRestoreRetryScheduler.Schedule(
                context,
                typeof(VpnRestoreRecoveryActivity),
                owner?.PackageName ?? context.PackageName ?? "com.agnosia.app",
                owner?.LaunchId,
                afterDeviceRestart: true);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Could not schedule VPN recovery after device restart: {exception}");
        }
    }
}
