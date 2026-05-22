using System.Security.Cryptography;
using System.Text.Json;
using Agnosia.Android.Activities;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Receivers;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Shortcuts;

internal static class HiddenAppShortcutManager
{
    private const string LogTag = "AgnosiaHiddenShortcut";
    private const string ExtraPackageName = "packageName";
    private const string ExtraShortcutToken = "shortcutToken";
    private const string ExtraTargetActivity = "targetActivity";
    private const string ExtraLabel = "label";
    private const string ExtraIconBase64 = "iconBase64";
    private const int ShortcutIconSizePixels = 192;
    private const int MetadataResolveAttempts = 12;
    private const int MetadataResolveDelayMilliseconds = 250;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<HiddenAppShortcutBuildResult> BuildMetadataAsync(
        Context context,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MetadataResolveAttempts; attempt++)
        {
            if (TryBuildMetadataCore(context, packageName, out var metadata, out var error))
            {
                if (attempt > 1)
                    Log.Info(LogTag, $"Shortcut metadata for {packageName} became available on attempt {attempt}.");

                return HiddenAppShortcutBuildResult.Success(metadata);
            }

            if (attempt == MetadataResolveAttempts)
            {
                Log.Warn(LogTag, $"Failed to prepare shortcut metadata for {packageName}.");
                return HiddenAppShortcutBuildResult.Failure(error);
            }

            await Task.Delay(MetadataResolveDelayMilliseconds, cancellationToken);
        }

        return HiddenAppShortcutBuildResult.Failure($"Не удалось подготовить данные ярлыка для {packageName}.");
    }

    public static ShortcutFreezePreparationResult CreateOrUpdatePinnedShortcut(Context context,
        HiddenAppShortcutMetadata metadata)
    {
        if (GetShortcutManager(context) is not { } shortcutManager)
            return ShortcutFreezePreparationResult.Failure("Android не предоставил сервис ярлыков.");

        WriteMetadata(metadata);

        var shortcutInfo = BuildShortcutInfo(context, metadata);
        if (IsPinned(shortcutManager, metadata.ShortcutId))
        {
            try
            {
                Log.Info(LogTag, $"Updating existing pinned shortcut {metadata.ShortcutId}.");
                shortcutManager.UpdateShortcuts([shortcutInfo]);
                shortcutManager.EnableShortcuts([metadata.ShortcutId]);
            }
            catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
            {
                Log.Warn(LogTag, $"Failed to update existing pinned shortcut {metadata.ShortcutId}: {exception}");
                return ShortcutFreezePreparationResult.Failure(
                    "Android не смог обновить ярлык скрытого приложения.");
            }

            return ShortcutFreezePreparationResult.Immediate("Приложение скрыто, ярлык обновлен.");
        }

        if (!shortcutManager.IsRequestPinShortcutSupported)
        {
            Log.Warn(LogTag, $"Launcher does not support pin shortcuts for {metadata.ShortcutId}.");
            return ShortcutFreezePreparationResult.Failure(
                "Текущий лаунчер не поддерживает закрепленные ярлыки для скрытых приложений.");
        }

        var callbackIntent = new Intent(context, typeof(ShortcutPinReceiver));
        callbackIntent.SetAction(AgnosiaActions.ShortcutPinned);
        callbackIntent.PutExtra(ExtraPackageName, metadata.TargetPackage);

        var callbackPendingIntent = PendingIntent.GetBroadcast(
            context,
            GetStableRequestCode(metadata.TargetPackage),
            callbackIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        if (callbackPendingIntent is null)
            return ShortcutFreezePreparationResult.Failure(
                "Android не смог подготовить обратный вызов для закрепления ярлыка.");

        Log.Info(LogTag, $"Requesting pin shortcut {metadata.ShortcutId} in the current profile.");
        var requested = shortcutManager.RequestPinShortcut(shortcutInfo, callbackPendingIntent.IntentSender);
        Log.Info(LogTag, $"requestPinShortcut result for {metadata.ShortcutId}: requested={requested}.");
        return requested
            ? ShortcutFreezePreparationResult.Deferred(
                "Подтвердите добавление ярлыка на главный экран. После подтверждения приложение будет скрыто.")
            : ShortcutFreezePreparationResult.Failure("Лаунчер отклонил запрос на создание ярлыка.");
    }

    public static void WriteMetadataToIntent(Intent intent, HiddenAppShortcutMetadata metadata)
    {
        intent.PutExtra(ExtraPackageName, metadata.TargetPackage);
        intent.PutExtra(ExtraTargetActivity, metadata.TargetActivity);
        intent.PutExtra(ExtraLabel, metadata.Label);
        intent.PutExtra(ExtraIconBase64, metadata.IconBase64);
        intent.PutExtra(ExtraShortcutToken, metadata.Token);
    }

    public static HiddenAppShortcutMetadata? TryReadMetadataFromIntent(Intent? intent)
    {
        if (intent is null)
            return null;

        var packageName = intent.GetStringExtra(ExtraPackageName);
        var label = intent.GetStringExtra(ExtraLabel);
        var iconBase64 = intent.GetStringExtra(ExtraIconBase64);
        var token = intent.GetStringExtra(ExtraShortcutToken);
        if (string.IsNullOrWhiteSpace(packageName)
            || string.IsNullOrWhiteSpace(label)
            || string.IsNullOrWhiteSpace(iconBase64)
            || string.IsNullOrWhiteSpace(token))
            return null;

        return new HiddenAppShortcutMetadata(
            GetShortcutId(packageName),
            packageName,
            intent.GetStringExtra(ExtraTargetActivity),
            label,
            iconBase64,
            token);
    }

    public static Intent CreateInternalLaunchIntent(string packageName,
        string? targetActivity = null,
        string? label = null)
    {
        var storedMetadata = ReadMetadata(packageName);

        var intent = new Intent(AgnosiaActions.LaunchAppProxy);
        intent.PutExtra(ExtraPackageName, packageName);

        var effectiveTargetActivity = targetActivity ?? storedMetadata?.TargetActivity;
        if (!string.IsNullOrWhiteSpace(effectiveTargetActivity))
            intent.PutExtra(ExtraTargetActivity, effectiveTargetActivity);

        var effectiveLabel = label ?? storedMetadata?.Label;
        if (!string.IsNullOrWhiteSpace(effectiveLabel)) intent.PutExtra(ExtraLabel, effectiveLabel);

        AuthenticationUtility.SignIntent(intent);
        return intent;
    }

    public static bool TryGetLaunchRequest(Intent? intent, out HiddenAppLaunchRequest request)
    {
        request = HiddenAppLaunchRequest.Empty;
        if (intent is null)
            return false;

        if (string.Equals(intent.Action, AgnosiaActions.LaunchAppProxy, StringComparison.Ordinal))
        {
            if (!AuthenticationUtility.CheckIntent(intent)) return false;

            var packageName = intent.GetStringExtra(ExtraPackageName);
            if (string.IsNullOrWhiteSpace(packageName)) return false;

            var storedMetadata = ReadMetadata(packageName);
            request = new HiddenAppLaunchRequest(
                packageName,
                intent.GetStringExtra(ExtraTargetActivity) ?? storedMetadata?.TargetActivity,
                intent.GetStringExtra(ExtraLabel) ?? storedMetadata?.Label ?? packageName);
            return true;
        }

        if (!string.Equals(intent.Action, AgnosiaActions.LaunchHiddenAppShortcut, StringComparison.Ordinal))
            return false;

        var shortcutPackage = intent.GetStringExtra(ExtraPackageName);
        var shortcutToken = intent.GetStringExtra(ExtraShortcutToken);
        if (string.IsNullOrWhiteSpace(shortcutPackage) || string.IsNullOrWhiteSpace(shortcutToken))
            return false;

        var pinnedMetadata = ReadMetadata(shortcutPackage);
        if (pinnedMetadata is null || !string.Equals(pinnedMetadata.Token, shortcutToken, StringComparison.Ordinal))
            return false;

        request = new HiddenAppLaunchRequest(
            pinnedMetadata.TargetPackage,
            pinnedMetadata.TargetActivity,
            pinnedMetadata.Label);

        return true;
    }

    public static void HandlePinnedShortcutConfirmation(Context context, string packageName)
    {
        try
        {
            if (AgnosiaUtilities.IsProfileOwner(context))
            {
                Log.Info(LogTag, $"Shortcut pin callback for {packageName} is being handled in the work profile.");
                FreezeLocally(context, packageName);
                Toast.MakeText(context, "Ярлык создан, приложение скрыто.", ToastLength.Short)?.Show();
                return;
            }

            Log.Info(LogTag, $"Shortcut pin callback for {packageName} is being forwarded to the work profile.");
            ForwardFreezeToManagedProfile(context, packageName);
            Toast.MakeText(context, "Ярлык создан, приложение скрывается в рабочем профиле.", ToastLength.Short)
                ?.Show();
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to complete shortcut pinning for {packageName}: {exception}");
            Toast.MakeText(context, $"Ярлык создан, но {packageName} не удалось скрыть.", ToastLength.Long)?.Show();
        }
    }

    private static void FreezeLocally(Context context, string packageName)
    {
        if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } manager)
            throw new InvalidOperationException("Android did not provide DevicePolicyManager.");

        var admin = AgnosiaUtilities.GetAdminComponent(context, typeof(AgnosiaDeviceAdminReceiver));
        AndroidPolicyApi.TrySetApplicationHidden(
            manager,
            admin,
            packageName,
            true,
            LogTag,
            out var error);
        if (error is not null) throw new InvalidOperationException(error);
    }

    private static void ForwardFreezeToManagedProfile(Context context, string packageName)
    {
        _ = Task.Run(() =>
        {
            var result = AndroidProfileCommandGateway.FreezePackageInWorkProfile(
                context,
                packageName,
                "Приложение скрыто.");
            if (!result.Succeeded)
                Log.Warn(LogTag, $"Android не смог скрыть {packageName} в рабочем профиле: {result.Message}");
        });
    }

    private static HiddenAppShortcutMetadata? ReadMetadata(string packageName)
    {
        var storageKey = GetStorageKey(packageName);
        var raw = LocalStorageManager.Instance.GetString(storageKey);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonSerializer.Deserialize<HiddenAppShortcutMetadata>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to read shortcut metadata for {packageName}: {exception.Message}");
            LocalStorageManager.Instance.Remove(storageKey);
            return null;
        }
    }

    private static void WriteMetadata(HiddenAppShortcutMetadata metadata)
    {
        var raw = JsonSerializer.Serialize(metadata, JsonOptions);
        LocalStorageManager.Instance.SetString(GetStorageKey(metadata.TargetPackage), raw);
    }

    private static bool TryBuildMetadataCore(Context context, string packageName,
        out HiddenAppShortcutMetadata metadata, out string error)
    {
        var existing = ReadMetadata(packageName);
        var label = ResolveLabel(context, packageName) ?? existing?.Label ?? packageName;
        var targetActivity = ResolveTargetActivity(context, packageName) ?? existing?.TargetActivity;
        var iconBase64 = ResolveIconBase64(context, packageName) ?? existing?.IconBase64;

        if (string.IsNullOrWhiteSpace(iconBase64))
        {
            metadata = HiddenAppShortcutMetadata.Empty;
            error = $"Не удалось подготовить иконку для {packageName}.";
            return false;
        }

        metadata = new HiddenAppShortcutMetadata(
            GetShortcutId(packageName),
            packageName,
            targetActivity,
            label,
            iconBase64,
            existing?.Token ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        error = string.Empty;
        return true;
    }

    private static ShortcutManager? GetShortcutManager(Context context)
    {
        return context.GetSystemService(Context.ShortcutService) as ShortcutManager;
    }

    private static bool IsPinned(ShortcutManager shortcutManager, string shortcutId)
    {
        return shortcutManager.PinnedShortcuts.Any(shortcut =>
            string.Equals(shortcut.Id, shortcutId, StringComparison.Ordinal));
    }

    private static ShortcutInfo BuildShortcutInfo(Context context, HiddenAppShortcutMetadata metadata)
    {
        var iconBytes = Convert.FromBase64String(metadata.IconBase64);
        using var iconBitmap = BitmapFactory.DecodeByteArray(iconBytes, 0, iconBytes.Length)
                               ?? throw new InvalidOperationException("Failed to decode the stored shortcut icon.");

        var launchIntent = new Intent(context, typeof(ProxyActivity));
        launchIntent.SetAction(AgnosiaActions.LaunchHiddenAppShortcut);
        launchIntent.PutExtra(ExtraPackageName, metadata.TargetPackage);
        launchIntent.PutExtra(ExtraShortcutToken, metadata.Token);

        if (!string.IsNullOrWhiteSpace(metadata.TargetActivity))
            launchIntent.PutExtra(ExtraTargetActivity, metadata.TargetActivity);

        launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);

        return new ShortcutInfo.Builder(context, metadata.ShortcutId)
            .SetShortLabel(metadata.Label)
            .SetLongLabel(metadata.Label)
            .SetIcon(Icon.CreateWithBitmap(iconBitmap))
            .SetIntent(launchIntent)
            .Build();
    }

    private static string? ResolveLabel(Context context, string packageName)
    {
        try
        {
            var packageManager = context.PackageManager;
            return packageManager?.GetApplicationInfo(packageName, PackageInfoFlags.MatchDisabledComponents) is not
                { } applicationInfo
                ? null
                : packageManager.GetApplicationLabel(applicationInfo);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    private static string? ResolveTargetActivity(Context context, string packageName)
    {
        var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(packageName);
        if (launchIntent?.Component is { } component && !string.IsNullOrWhiteSpace(component.ClassName))
            return component.ClassName;

        var resolveInfo = launchIntent is null
            ? null
            : context.PackageManager?.ResolveActivity(launchIntent, AndroidSystemApi.GetQueryIntentActivityFlags());

        return resolveInfo?.ActivityInfo?.Name;
    }

    private static string? ResolveIconBase64(Context context, string packageName)
    {
        try
        {
            var drawable = context.PackageManager?.GetApplicationIcon(packageName);
            if (drawable is null)
                return null;

            using var bitmap = RenderDrawable(drawable);
            using var stream = new MemoryStream();
            bitmap.Compress(
                Bitmap.CompressFormat.Png ?? throw new InvalidOperationException("PNG compress format is unavailable."),
                100, stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception exception) when
            (exception is PackageManager.NameNotFoundException or InvalidOperationException)
        {
            Log.Warn(LogTag, $"Failed to load icon for {packageName}: {exception.Message}");
            return null;
        }
    }

    private static Bitmap RenderDrawable(Drawable drawable)
    {
        if (drawable is BitmapDrawable bitmapDrawable && bitmapDrawable.Bitmap is { } existingBitmap)
            return Bitmap.CreateScaledBitmap(existingBitmap, ShortcutIconSizePixels, ShortcutIconSizePixels, true);


        var bitmap = Bitmap.CreateBitmap(
            ShortcutIconSizePixels,
            ShortcutIconSizePixels,
            Bitmap.Config.Argb8888 ?? throw new InvalidOperationException("ARGB8888 bitmap config is unavailable."));
        using var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
        drawable.Draw(canvas);
        return bitmap;
    }

    private static string GetShortcutId(string packageName)
    {
        return $"hidden:{packageName}";
    }

    private static string GetStorageKey(string packageName)
    {
        return $"{StorageKeys.HiddenShortcutMetadataPrefix}{packageName}";
    }

    private static int GetStableRequestCode(string packageName)
    {
        unchecked
        {
            const int offsetBasis = unchecked((int)2166136261);
            const int prime = 16777619;

            var hash = offsetBasis;
            foreach (var symbol in packageName)
            {
                hash ^= symbol;
                hash *= prime;
            }

            return hash & int.MaxValue;
        }
    }
}
