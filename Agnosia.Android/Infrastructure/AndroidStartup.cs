using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Android.Vpn;
using Agnosia.Infrastructure;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Infrastructure;

internal static class AndroidStartup
{
    private static readonly TimeSpan WorkProfilePolicyRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly object WorkProfilePolicyRefreshSync = new();
    private static DateTimeOffset _lastWorkProfilePolicyRefreshUtc = DateTimeOffset.MinValue;

    public static void ConfigurePrimaryProfileServices(Context context)
    {
        AgnosiaRuntime.Initialize(context);

        var startupState = ServiceRegistry.GetRequiredService<AppStartupState>();
        startupState.SuppressPrimaryUiStartup = false;
        startupState.InitialTheme = AndroidSettingsStore.LoadAppTheme(
            ServiceRegistry.GetRequiredService<LocalStorageManager>());
        AgnosiaUtilities.ApplyCrossProfileFileShuttleComponentState(context);
    }

    public static void SuppressPrimaryUiStartup()
    {
        ServiceRegistry.GetRequiredService<AppStartupState>().SuppressPrimaryUiStartup = true;
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
        LockdownVpnController.EnsureEnabledPolicy(context, typeof(AgnosiaDeviceAdminReceiver));
        MarkWorkProfilePoliciesRefreshed();
    }

    public static void EnsureWorkProfilePolicies(Context context, bool enableLauncher = false)
    {
        if (!enableLauncher && !ShouldRefreshWorkProfilePolicies()) return;

        EnforceWorkProfilePolicies(context, enableLauncher);
    }

    public static void EnforceWorkProfilePoliciesAndStartLockFreezeMonitor(
        Context context,
        bool enableLauncher = false)
    {
        EnforceWorkProfilePolicies(context, enableLauncher);
        WorkProfileLockFreezeService.EnsureRunning(context);
        LockFreezeCleanupJobService.RunStartupSafetyNet(context);
    }

    public static void EnsureWorkProfilePoliciesAndStartLockFreezeMonitor(
        Context context,
        bool enableLauncher = false)
    {
        EnsureWorkProfilePolicies(context, enableLauncher);
        WorkProfileLockFreezeService.EnsureRunning(context);
        LockFreezeCleanupJobService.RunStartupSafetyNet(context);
    }

    private static bool ShouldRefreshWorkProfilePolicies()
    {
        lock (WorkProfilePolicyRefreshSync)
        {
            return DateTimeOffset.UtcNow - _lastWorkProfilePolicyRefreshUtc >= WorkProfilePolicyRefreshInterval;
        }
    }

    private static void MarkWorkProfilePoliciesRefreshed()
    {
        lock (WorkProfilePolicyRefreshSync)
        {
            _lastWorkProfilePolicyRefreshUtc = DateTimeOffset.UtcNow;
        }
    }
}
