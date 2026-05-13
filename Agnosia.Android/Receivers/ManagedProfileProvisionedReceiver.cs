using Agnosia.Android.Api;
using Android.App.Admin;
using Android.Content;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(
    Name = "com.agnosia.app.ManagedProfileProvisionedReceiver",
    Exported = true,
    Label = "@string/app_name")]
[IntentFilter([DevicePolicyManager.ActionManagedProfileProvisioned])]
public sealed class ManagedProfileProvisionedReceiver : BroadcastReceiver
{
    private const string LogTag = "AgnosiaProfileProvisioned";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        if (!string.Equals(
                intent?.Action,
                DevicePolicyManager.ActionManagedProfileProvisioned,
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            AndroidPlatformBridge.Instance.NotifyManagedProfileProvisioned(context, intent);
            Log.Info(LogTag, "Primary-profile managed-profile provisioned broadcast recorded.");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to handle managed-profile provisioned broadcast: {exception}");
        }
    }
}
