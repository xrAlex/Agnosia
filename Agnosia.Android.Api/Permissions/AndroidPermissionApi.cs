using Agnosia.Android.Api.Platform;
using Agnosia.Models;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using Uri = Android.Net.Uri;

namespace Agnosia.Android.Api.Permissions;

public static class AndroidPermissionApi
{
    private const string LogTag = "AgnosiaPermissions";
    private const int NotificationPermissionRequestCode = 0x57C32;

    public static bool HasNotificationPermission(Activity activity)
    {
        try
        {
            return !OperatingSystem.IsAndroidVersionAtLeast(33)
                   || activity.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to check notification permission: {exception}");
            return false;
        }
    }

    public static OperationResult RequestNotificationPermission(Activity activity)
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33)
                && activity.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
                activity.RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);

            return OperationResult.Success("Подтвердите разрешение на уведомления в системном диалоге.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to request notification permission: {exception}");
            return OperationResult.Failure("Android не смог открыть запрос разрешения на уведомления.");
        }
    }

    public static bool HasOverlayPermission(Context context)
    {
        try
        {
            return Settings.CanDrawOverlays(context);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to check overlay permission: {exception}");
            return false;
        }
    }

    public static bool HasAllFilesAccess(Context context)
    {
        try
        {
            return !OperatingSystem.IsAndroidVersionAtLeast(30) || global::Android.OS.Environment.IsExternalStorageManager;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to check all-files access: {exception}");
            return false;
        }
    }

    public static OperationResult OpenAllFilesAccessSettings(Activity activity)
    {
        try
        {
            var packageName = activity.PackageName;
            if (string.IsNullOrWhiteSpace(packageName))
                return OperationResult.Failure("Не удалось определить имя пакета приложения.");

            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(Uri.FromParts("package", packageName, null));

            if (!AndroidIntentApi.TryStartActivity(
                    activity,
                    intent,
                    LogTag,
                    "Android не смог открыть настройки доступа ко всем файлам.",
                    out var error))
                return OpenAllFilesAccessFallbackSettings(activity, error);

            return OperationResult.Success("Включите доступ ко всем файлам для Agnosia.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to open all-files access settings: {exception}");
            return OpenAllFilesAccessFallbackSettings(activity, "Android не смог открыть настройки доступа ко всем файлам.");
        }
    }

    public static OperationResult OpenAppDetailsSettings(Activity activity)
    {
        try
        {
            var packageName = activity.PackageName;

            if (string.IsNullOrWhiteSpace(packageName))
                return OperationResult.Failure("Не удалось определить имя пакета приложения.");

            var uri = Uri.FromParts("package", packageName, null);
            var intent = new Intent(Settings.ActionApplicationDetailsSettings);

            intent.SetData(uri);
            activity.StartActivity(intent);

            return OperationResult.Success(
                "Пролистайте вниз и включите разрешение «Поверх других приложений» вручную.");
        }
        catch (ActivityNotFoundException exception)
        {
            Log.Warn(LogTag, $"Settings activity not found: {exception}");
            return OperationResult.Failure("Android не нашёл страницу настроек приложения.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to open app details settings: {exception}");
            return OperationResult.Failure("Android не смог открыть страницу настроек приложения.");
        }
    }

    public static OperationResult OpenOverlaySettings(Activity activity)
    {
        try
        {
            var packageName = activity.PackageName;
            if (string.IsNullOrWhiteSpace(packageName))
                return OperationResult.Failure("Не удалось определить имя пакета приложения.");

            return OpenAppDetailsSettings(activity);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to open overlay settings: {exception}");
            return OpenAppDetailsSettings(activity);
        }
    }

    private static OperationResult OpenAllFilesAccessFallbackSettings(Activity activity, string? originalError)
    {
        try
        {
            var intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
            activity.StartActivity(intent);
            return OperationResult.Success("Откройте Agnosia в списке и включите доступ ко всем файлам.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to open fallback all-files access settings: {exception}");
            return OperationResult.Failure(originalError ?? "Android не смог открыть настройки доступа ко всем файлам.");
        }
    }
}
