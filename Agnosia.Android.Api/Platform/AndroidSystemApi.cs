using Android.App.Admin;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;

namespace Agnosia.Android.Api;

public static class AndroidSystemApi
{
    private const string ActivityServiceName = "activity";
    private const string AppOpsServiceName = "appops";
    private const string ConnectivityServiceName = "connectivity";
    private const string DevicePolicyServiceName = "device_policy";
    private const string NotificationServiceName = "notification";
    private const string UsageStatsServiceName = "usagestats";

    public static ActivityManager? GetActivityManager(Context context) =>
        context.GetSystemService(ActivityServiceName) as ActivityManager;

    public static AppOpsManager? GetAppOpsManager(Context context) =>
        context.GetSystemService(AppOpsServiceName) as AppOpsManager;

    public static UsageStatsManager? GetUsageStatsManager(Context context) =>
        context.GetSystemService(UsageStatsServiceName) as UsageStatsManager;

    public static DevicePolicyManager? GetDevicePolicyManager(Context context) =>
        context.GetSystemService(DevicePolicyServiceName) as DevicePolicyManager;

    public static ConnectivityManager? GetConnectivityManager(Context context) =>
        context.GetSystemService(ConnectivityServiceName) as ConnectivityManager;

    public static NotificationManager? GetNotificationManager(Context context) =>
        context.GetSystemService(NotificationServiceName) as NotificationManager;

    public static PowerManager? GetPowerManager(Context context) =>
        context.GetSystemService(Context.PowerService) as PowerManager;

    public static CrossProfileApps? GetCrossProfileApps(Context context) =>
        context.GetSystemService(Context.CrossProfileAppsService) as CrossProfileApps;

    public static PackageInfoFlags GetQueryIntentActivityFlags() =>
        PackageInfoFlags.MatchAll;

    public static PackageInfoFlags GetInstalledApplicationFlags() =>
        PackageInfoFlags.MatchDisabledComponents | PackageInfoFlags.MatchUninstalledPackages;

    public static bool IsCrossProfileIntentForwarder(ResolveInfo resolveInfo) =>
        resolveInfo.ActivityInfo is not null
        && resolveInfo.IsCrossProfileIntentForwarderActivity;
}
