using Android.App.Admin;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;

namespace Agnosia.Android.Api.Platform;

public static class AndroidSystemApi
{
    private const string ActivityServiceName = "activity";
    private const string AppOpsServiceName = "appops";
    private const string ConnectivityServiceName = "connectivity";
    private const string DevicePolicyServiceName = "device_policy";
    private const string NotificationServiceName = "notification";
    private const string UsageStatsServiceName = "usagestats";
    private const string UserServiceName = "user";

    public static ActivityManager? GetActivityManager(Context context)
    {
        return context.GetSystemService(ActivityServiceName) as ActivityManager;
    }

    public static AppOpsManager? GetAppOpsManager(Context context)
    {
        return context.GetSystemService(AppOpsServiceName) as AppOpsManager;
    }

    public static UsageStatsManager? GetUsageStatsManager(Context context)
    {
        return context.GetSystemService(UsageStatsServiceName) as UsageStatsManager;
    }

    public static DevicePolicyManager? GetDevicePolicyManager(Context context)
    {
        return context.GetSystemService(DevicePolicyServiceName) as DevicePolicyManager;
    }

    public static ConnectivityManager? GetConnectivityManager(Context context)
    {
        return context.GetSystemService(ConnectivityServiceName) as ConnectivityManager;
    }

    public static NotificationManager? GetNotificationManager(Context context)
    {
        return context.GetSystemService(NotificationServiceName) as NotificationManager;
    }

    public static PowerManager? GetPowerManager(Context context)
    {
        return context.GetSystemService(Context.PowerService) as PowerManager;
    }

    public static UserManager? GetUserManager(Context context)
    {
        return context.GetSystemService(UserServiceName) as UserManager;
    }

    public static CrossProfileApps? GetCrossProfileApps(Context context)
    {
        return context.GetSystemService(Context.CrossProfileAppsService) as CrossProfileApps;
    }

    public static PackageInfoFlags GetQueryIntentActivityFlags()
    {
        return PackageInfoFlags.MatchAll;
    }

    public static PackageInfoFlags GetInstalledApplicationFlags()
    {
        return PackageInfoFlags.MatchDisabledComponents | PackageInfoFlags.MatchUninstalledPackages;
    }

    public static bool IsCrossProfileIntentForwarder(ResolveInfo resolveInfo)
    {
        return resolveInfo.ActivityInfo is not null
               && resolveInfo.IsCrossProfileIntentForwarderActivity;
    }
}