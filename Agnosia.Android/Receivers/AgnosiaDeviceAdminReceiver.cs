using Agnosia.Android.Api.Platform;
using Agnosia.Android.Infrastructure;
using Android;
using Android.App.Admin;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(
    Name = "com.agnosia.app.AgnosiaDeviceAdminReceiver",
    Permission = Manifest.Permission.BindDeviceAdmin,
    Exported = true,
    Label = "@string/app_name",
    Description = "@string/device_admin_receiver_description")]
[MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
[IntentFilter(
[
    ActionDeviceAdminEnabled,
    ActionDeviceAdminDisabled,
    ActionProfileProvisioningComplete
])]
public sealed class AgnosiaDeviceAdminReceiver : DeviceAdminReceiver
{
    private const string LogTag = "AgnosiaDeviceAdmin";

    public override void OnProfileProvisioningComplete(Context context, Intent intent)
    {
        base.OnProfileProvisioningComplete(context, intent);
        AgnosiaRuntime.Initialize(context);

        try
        {
            if (!AgnosiaUtilities.IsProfileOwner(context))
            {
                Log.Warn(LogTag, "Profile provisioning completed, but Agnosia is not the profile owner.");
                return;
            }

            if (!AuthenticationUtility.TryStoreProvisioningAuthKey(intent))
            {
                Log.Warn(LogTag, "Profile provisioning completed without a valid Agnosia authentication key.");
                return;
            }

            AndroidStartup.EnforceWorkProfilePolicies(context, true);
            Log.Info(LogTag,
                "Work profile provisioning completed and profile enabled; parent profile will observe provisioning through the managed-profile broadcast.");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Work profile provisioning finalization failed: {exception}");
        }
    }
}
