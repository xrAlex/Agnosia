using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Infrastructure;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Infrastructure;

internal static class AndroidStartup
{
    public static void ConfigurePrimaryProfileServices()
    {
        ServiceRegistry.SuppressPrimaryUiStartup = false;
        ServiceRegistry.PlatformBridge = AndroidPlatformBridge.Instance;
        ServiceRegistry.InitialTheme = AndroidSettingsStore.LoadAppTheme(LocalStorageManager.Instance);
    }

    public static void SuppressPrimaryUiStartup()
    {
        ServiceRegistry.SuppressPrimaryUiStartup = true;
    }

    public static bool TryIsProfileOwner(Context context, string logTag, string failureContext)
    {
        try
        {
            return AgnosiaUtilities.IsProfileOwner(context);
        }
        catch (Exception exception)
        {
            Log.Warn(logTag, $"{failureContext}: {exception.Message}");
            return false;
        }
    }

    public static void EnforceWorkProfilePolicies(Context context, bool enableLauncher = false)
    {
        AgnosiaUtilities.EnforceWorkProfilePolicies(
            context,
            typeof(AgnosiaDeviceAdminReceiver),
            MainActivity.LauncherActivityName,
            enableLauncher);
        AgnosiaUtilities.EnforceUserRestrictions(context, typeof(AgnosiaDeviceAdminReceiver));
    }

    public static void EnforceWorkProfilePoliciesAndStartLockFreezeMonitor(
        Context context,
        bool enableLauncher = false)
    {
        EnforceWorkProfilePolicies(context, enableLauncher);
        WorkProfileLockFreezeService.EnsureRunning(context);
        LockFreezeCleanupJobService.RunStartupSafetyNet(context);
    }
}
