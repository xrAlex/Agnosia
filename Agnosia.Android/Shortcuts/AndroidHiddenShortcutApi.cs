using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Shortcuts;

internal static class AndroidHiddenShortcutApi
{
    private const string LogTag = "AgnosiaHiddenShortcut";

    public static OperationResult InvalidatePinnedShortcut(Context context, string packageName)
    {
        var shortcutId = GetShortcutId(packageName);
        RemoveMetadata(packageName);

        if (GetShortcutManager(context) is not { } shortcutManager)
        {
            Log.Warn(LogTag, $"Shortcut service unavailable while invalidating {shortcutId}.");
            return OperationResult.Failure(
                "Android не предоставил сервис ярлыков. Удалите ярлык с главного экрана вручную.");
        }

        try
        {
            using var disabledMessage = new Java.Lang.String("Это приложение удалено из рабочего профиля.");
            shortcutManager.DisableShortcuts([shortcutId], disabledMessage);
            Log.Info(LogTag, $"Disabled pinned shortcut {shortcutId} after package removal.");
            return OperationResult.Success(
                "Ярлык отключен. Если launcher оставил иконку на главном экране, удалите ее вручную.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to disable pinned shortcut {shortcutId}: {exception}");
            return OperationResult.Failure(
                "Android не смог отключить ярлык. Удалите его с главного экрана вручную.");
        }
    }

    private static ShortcutManager? GetShortcutManager(Context context)
    {
        return context.GetSystemService(Context.ShortcutService) as ShortcutManager;
    }

    private static void RemoveMetadata(string packageName)
    {
        ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(GetStorageKey(packageName));
    }

    private static string GetShortcutId(string packageName)
    {
        return $"hidden:{packageName}";
    }

    private static string GetStorageKey(string packageName)
    {
        return $"{StorageKeys.HiddenShortcutMetadataPrefix}{packageName}";
    }
}
