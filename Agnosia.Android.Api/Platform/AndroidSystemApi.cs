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
        return GetSystemService<ActivityManager>(context, ActivityServiceName);
    }

    public static AppOpsManager? GetAppOpsManager(Context context)
    {
        return GetSystemService<AppOpsManager>(context, AppOpsServiceName);
    }

    public static UsageStatsManager? GetUsageStatsManager(Context context)
    {
        return GetSystemService<UsageStatsManager>(context, UsageStatsServiceName);
    }

    public static DevicePolicyManager? GetDevicePolicyManager(Context context)
    {
        return GetSystemService<DevicePolicyManager>(context, DevicePolicyServiceName);
    }

    public static ConnectivityManager? GetConnectivityManager(Context context)
    {
        return GetSystemService<ConnectivityManager>(context, ConnectivityServiceName);
    }

    public static NotificationManager? GetNotificationManager(Context context)
    {
        return GetSystemService<NotificationManager>(context, NotificationServiceName);
    }

    public static PowerManager? GetPowerManager(Context context)
    {
        return GetSystemService<PowerManager>(context, Context.PowerService);
    }

    public static UserManager? GetUserManager(Context context)
    {
        return GetSystemService<UserManager>(context, UserServiceName);
    }

    public static CrossProfileApps? GetCrossProfileApps(Context context)
    {
        return GetSystemService<CrossProfileApps>(context, Context.CrossProfileAppsService);
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

    private static T? GetSystemService<T>(Context context, string serviceName)
        where T : class
    {
        return context.GetSystemService(serviceName) as T;
    }
}
