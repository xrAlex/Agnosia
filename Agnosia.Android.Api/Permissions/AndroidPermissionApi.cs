using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Platform;
using Agnosia.Models;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Provider;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using Uri = Android.Net.Uri;

namespace Agnosia.Android.Api.Permissions;

internal static class AndroidPermissionApi
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

    public static bool IsVpnPrepared(Activity activity)
    {
        try
        {
            return VpnService.Prepare(activity) is null;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to check VPN preparation state: {exception}");
            return false;
        }
    }

    public static async Task<OperationResult> RequestVpnControlAsync(
        AndroidActivityCommandGateway activityCommands,
        CancellationToken cancellationToken)
    {
        var activity = activityCommands.CurrentActivity;
        Intent? prepareIntent;
        try
        {
            prepareIntent = VpnService.Prepare(activity);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to prepare VPN permission request: {exception}");
            return OperationResult.Failure("Android не смог открыть запрос доступа к VPN.");
        }

        if (prepareIntent is null) return OperationResult.Success("VPN-доступ уже подготовлен.");

        var result = await activityCommands.StartExternalActivityForResultAsync(prepareIntent, cancellationToken);
        var error = AndroidActivityResultApi.ExtractError(result);
        if (!string.IsNullOrWhiteSpace(error)) return OperationResult.Failure(error);

        return result.ResultCode == Result.Ok
            ? OperationResult.Success("VPN-доступ подготовлен.")
            : OperationResult.Failure("Android не выдал доступ к VPN.");
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
}