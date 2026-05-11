using Agnosia.Android.Api;
using Agnosia.Android.Services;
using Android;
using Android.App.Admin;
using Android.Content;
using Log = Agnosia.Android.Api.AgnosiaLog;

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

            if (!AndroidProvisioningApi.TryStoreProvisioningAuthKey(intent))
            {
                Log.Warn(LogTag, "Profile provisioning completed without a valid Agnosia authentication key.");
                return;
            }

            AgnosiaUtilities.EnforceWorkProfilePolicies(
                context,
                typeof(AgnosiaDeviceAdminReceiver),
                typeof(MainActivity),
                enableProfile: true);
            AgnosiaUtilities.EnforceUserRestrictions(context, typeof(AgnosiaDeviceAdminReceiver));
            WorkProfileLockFreezeService.EnsureRunning(context);
            Log.Info(LogTag, "Work profile provisioning finalized and profile enabled.");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Work profile provisioning finalization failed: {exception}");
        }
    }
}
