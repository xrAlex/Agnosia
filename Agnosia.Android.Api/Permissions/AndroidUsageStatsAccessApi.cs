using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Platform;
using Android.Content;
using Android.Provider;

namespace Agnosia.Android.Api.Permissions;

public static class AndroidUsageStatsAccessApi
{
    public static bool HasAccess(
        Context context,
        string logTag,
        bool fallbackWhenUnavailable = true,
        bool logFailure = true)
    {
        try
        {
            if (AndroidSystemApi.GetAppOpsManager(context) is not { } appOpsManager) return fallbackWhenUnavailable;

            var uid = context.ApplicationInfo?.Uid ?? -1;
            var packageName = context.PackageName;
            if (uid < 0 || string.IsNullOrWhiteSpace(packageName)) return fallbackWhenUnavailable;

            var mode = appOpsManager.CheckOpNoThrow(AppOpsManager.OpstrGetUsageStats, uid, packageName);
            return mode is AppOpsManagerMode.Allowed or AppOpsManagerMode.Foreground;
        }
        catch (Exception exception)
        {
            if (logFailure) AgnosiaLog.Warn(logTag, $"Failed to check usage stats access: {exception}");

            return fallbackWhenUnavailable;
        }
    }

    public static bool TryOpenSettings(Activity activity, string logTag, Action<string> onError)
    {
        if (AndroidIntentApi.TryStartActivity(
                activity,
                new Intent(Settings.ActionUsageAccessSettings),
                logTag,
                "Android не смог открыть настройки доступа к истории использования.",
                out var error))
            return true;

        onError(error ?? "Android не смог открыть настройки доступа к истории использования.");
        return false;
    }
}