using System.Security.Cryptography;
using System.Text;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Models;
using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Path = System.IO.Path;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Packages;

public static class AndroidAppInventoryApi
{
    private const string LogTag = "AndroidAppInventory";
    private const int AppIconSizePixels = 48;
    private const int MaxIconCacheEntries = 512;
    private const string IconCacheDirectoryName = "app-icons";
    private const string IconCacheFileExtension = ".png";
    private const string MissingIconCacheFileExtension = ".missing";
    private const int RequestedPermissionGrantedFlag = 2;
    private const string CameraOp = "android:camera";
    private const string FineLocationOp = "android:fine_location";
    private const string CoarseLocationOp = "android:coarse_location";
    private const string MicrophoneOp = "android:record_audio";
    private const string UsageStatsOp = "android:get_usage_stats";
    private const string SystemAlertWindowOp = "android:system_alert_window";
    private static readonly Lock IconCacheSync = new();
    private static readonly Dictionary<string, AppIconCacheEntry> IconCache = new(StringComparer.Ordinal);

    public static List<AppServiceModel> QueryInstalledApps(
        Context context,
        PackageManager packageManager,
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        bool showAll,
        CancellationToken cancellationToken = default)
    {
        var apps = packageManager.GetInstalledApplications(AndroidSystemApi.GetInstalledApplicationFlags());
        var models = new List<AppServiceModel>(apps.Count);
        var installedPackageNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(app.PackageName)) installedPackageNames.Add(app.PackageName);

            if (TryCreateModel(context, packageManager, policyManager, admin, app, showAll) is
                { } model) models.Add(model);
        }

        PruneMemoryIconCache(installedPackageNames);
        models.Sort(static (left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Label, right.Label));
        return models;
    }

    public static byte[]? LoadAppIconPng(
        Context context,
        PackageManager packageManager,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsCurrentOrInvalidPackage(context, packageName)) return null;

        try
        {
            if (!TryGetPackageIdentity(packageManager, packageName, out var identity)) return null;

            var app = TryGetApplicationInfo(packageManager, packageName,
                          AndroidSystemApi.GetInstalledApplicationFlags())
                      ?? packageManager.GetApplicationInfo(packageName, PackageInfoFlags.MatchDisabledComponents);
            cancellationToken.ThrowIfCancellationRequested();

            return ResolveAppIconPng(context, packageManager, app, packageName, identity, cancellationToken);
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                              or InvalidOperationException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    public static byte[]? TryLoadCachedAppIconPng(
        Context context,
        PackageManager packageManager,
        string packageName)
    {
        if (IsCurrentOrInvalidPackage(context, packageName)) return null;

        try
        {
            return TryGetPackageIdentity(packageManager, packageName, out var identity)
                   && TryGetCachedIcon(context, packageName, identity, out var cachedIcon)
                ? cachedIcon
                : null;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                              or InvalidOperationException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static AppServiceModel? TryCreateModel(
        Context context,
        PackageManager packageManager,
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        ApplicationInfo app,
        bool showAll)
    {
        if (!TryGetPackageName(context, app, out var packageName)) return null;

        var isSystem = AndroidWorkProfilePackageClassifier.IsSystemApp(app);
        var isInstalled = (app.Flags & ApplicationInfoFlags.Installed) != 0;
        var isHidden = TryIsApplicationHidden(policyManager, admin, packageName);
        if (!showAll && (isSystem || (!isInstalled && !isHidden))) return null;

        var permissionRisk = AppPermissionRiskAnalysis.Safe;
        if (isSystem)
        {
            if (!TryGetPackageIdentity(packageManager, packageName, out var identity)) return null;

            return CreateModel(
                context,
                packageManager,
                app,
                packageName,
                true,
                isHidden,
                isInstalled,
                identity,
                permissionRisk);
        }

        if (!TryGetPackageInventoryDetails(
                context,
                packageManager,
                packageName,
                out var packageIdentity,
                out permissionRisk)) return null;

        return CreateModel(
            context,
            packageManager,
            app,
            packageName,
            isSystem,
            isHidden,
            isInstalled,
            packageIdentity,
            permissionRisk);
    }

    private static AppServiceModel CreateModel(
        Context context,
        PackageManager packageManager,
        ApplicationInfo app,
        string packageName,
        bool isSystem,
        bool isHidden,
        bool isInstalled,
        PackageIdentity packageIdentity,
        AppPermissionRiskAnalysis permissionRisk)
    {
        return new AppServiceModel
        {
            PackageName = packageName,
            Label = packageManager.GetApplicationLabel(app),
            SourceDirectory = app.SourceDir,
            SplitApks = app.SplitSourceDirs?.ToArray() ?? [],
            IsSystem = isSystem,
            IsHidden = isHidden,
            CanLaunch = packageManager.GetLaunchIntentForPackage(packageName) is not null,
            IsInstalled = isInstalled,
            PermissionRiskLevel = permissionRisk.Level,
            RiskyPermissions = permissionRisk.RiskyPermissions.ToArray(),
            MatchedPermissionRiskRuleIds = permissionRisk.MatchedRuleIds.ToArray(),
            PermissionRiskScore = permissionRisk.Score,
            PermissionRiskRawScore = permissionRisk.RawScore,
            PermissionRiskConfidence = permissionRisk.Confidence,
            PermissionRiskScoreBreakdown = permissionRisk.ScoreBreakdown,
            ManifestPermissions = permissionRisk.ManifestPermissions.ToArray(),
            RuntimePermissions = permissionRisk.RuntimePermissions.ToArray(),
            IconPng = TryGetCachedIcon(context, packageName, packageIdentity, out var cachedIcon)
                ? cachedIcon
                : AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(context, packageManager, packageName)
        };
    }

    private static bool IsCurrentOrInvalidPackage(Context context, string? packageName)
    {
        return string.IsNullOrWhiteSpace(packageName)
               || string.Equals(packageName, context.PackageName, StringComparison.Ordinal);
    }

    private static bool TryGetPackageName(Context context, ApplicationInfo app, out string packageName)
    {
        var candidate = app.PackageName;
        if (IsCurrentOrInvalidPackage(context, candidate))
        {
            packageName = null!;
            return false;
        }

        packageName = candidate!;
        return true;
    }

    private static bool TryIsApplicationHidden(
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        string packageName)
    {
        if (policyManager is null || admin is null) return false;

        try
        {
            return policyManager.IsApplicationHidden(admin, packageName);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    private static ApplicationInfo? TryGetApplicationInfo(
        PackageManager packageManager,
        string packageName,
        PackageInfoFlags flags)
    {
        try
        {
            return packageManager.GetApplicationInfo(packageName, flags);
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static byte[]? ResolveAppIconPng(
        Context context,
        PackageManager packageManager,
        ApplicationInfo app,
        string packageName,
        PackageIdentity identity,
        CancellationToken cancellationToken)
    {
        if (TryGetCachedIcon(context, packageName, identity, out var cachedIcon)) return cachedIcon;

        cancellationToken.ThrowIfCancellationRequested();
        var iconPng = TryRenderAppIcon(context, packageManager, app, packageName);

        cancellationToken.ThrowIfCancellationRequested();
        CacheIcon(context, packageName, identity, iconPng);

        return iconPng;
    }

    private static Drawable? TryGetApplicationIcon(PackageManager packageManager, ApplicationInfo app)
    {
        try
        {
            return packageManager.GetApplicationIcon(app);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryRenderAppIcon(
        Context context,
        PackageManager packageManager,
        ApplicationInfo app,
        string packageName)
    {
        try
        {
            using var drawable = TryGetApplicationIcon(packageManager, app) ?? ResolveLauncherIcon(context, packageName);
            if (drawable is null) return null;

            using var bitmap = RenderAppIcon(drawable);
            using var stream = new MemoryStream();
            bitmap.Compress(
                Bitmap.CompressFormat.Png ?? throw new InvalidOperationException("PNG compress format is unavailable."),
                85,
                stream);
            return stream.ToArray();
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static Drawable? ResolveLauncherIcon(Context context, string packageName)
    {
        if (context.GetSystemService(Context.LauncherAppsService) is not LauncherApps launcherApps) return null;

        try
        {
            var activities = launcherApps.GetActivityList(packageName, Process.MyUserHandle());
            return activities?.FirstOrDefault()?.GetIcon(0);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static bool TryGetCachedIcon(
        Context context,
        string packageName,
        PackageIdentity identity,
        out byte[]? iconPng)
    {
        lock (IconCacheSync)
        {
            if (IconCache.TryGetValue(packageName, out var entry) && entry.Identity == identity)
            {
                if (entry.IconPng is { Length: > 0 })
                {
                    iconPng = entry.IconPng;
                    return true;
                }

                IconCache.Remove(packageName);
                iconPng = null;
                return false;
            }
        }

        var cacheKey = GetIconCacheKey(packageName, identity);
        if (TryReadDiskCachedIcon(context, cacheKey, out iconPng))
        {
            lock (IconCacheSync)
            {
                IconCache[packageName] = new AppIconCacheEntry(identity, iconPng);
            }

            return true;
        }

        return false;
    }

    private static bool TryReadDiskCachedIcon(Context context, string cacheKey, out byte[]? iconPng)
    {
        iconPng = null;
        var directory = GetIconCacheDirectory(context);
        if (directory is null) return false;

        var iconPath = Path.Combine(directory, cacheKey + IconCacheFileExtension);
        if (File.Exists(iconPath))
            try
            {
                iconPng = File.ReadAllBytes(iconPath);
                return iconPng.Length > 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }

        var missingPath = Path.Combine(directory, cacheKey + MissingIconCacheFileExtension);
        if (File.Exists(missingPath))
            try
            {
                File.Delete(missingPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

        return false;
    }

    private static void CacheIcon(
        Context context,
        string packageName,
        PackageIdentity identity,
        byte[]? iconPng)
    {
        if (iconPng is not { Length: > 0 })
        {
            lock (IconCacheSync)
            {
                IconCache.Remove(packageName);
            }

            WriteDiskCachedIcon(context, GetIconCacheKey(packageName, identity), null);
            return;
        }

        lock (IconCacheSync)
        {
            IconCache[packageName] = new AppIconCacheEntry(identity, iconPng);
        }

        WriteDiskCachedIcon(context, GetIconCacheKey(packageName, identity), iconPng);
    }

    private static void WriteDiskCachedIcon(Context context, string cacheKey, byte[]? iconPng)
    {
        var directory = GetOrCreateIconCacheDirectory(context);
        if (directory is null) return;

        try
        {
            var iconPath = Path.Combine(directory, cacheKey + IconCacheFileExtension);
            var missingPath = Path.Combine(directory, cacheKey + MissingIconCacheFileExtension);
            if (iconPng is { Length: > 0 })
            {
                File.WriteAllBytes(iconPath, iconPng);
                File.Delete(missingPath);
                return;
            }

            File.Delete(missingPath);
            File.Delete(iconPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void PruneMemoryIconCache(HashSet<string> installedPackageNames)
    {
        lock (IconCacheSync)
        {
            foreach (var packageName in IconCache.Keys
                         .Where(packageName => !installedPackageNames.Contains(packageName))
                         .ToArray()) IconCache.Remove(packageName);

            foreach (var packageName in IconCache.Keys.Take(Math.Max(0, IconCache.Count - MaxIconCacheEntries))
                         .ToArray()) IconCache.Remove(packageName);
        }
    }

    private static Bitmap RenderAppIcon(Drawable drawable)
    {
        if (drawable is BitmapDrawable { Bitmap: { } existingBitmap })
            return Bitmap.CreateScaledBitmap(existingBitmap, AppIconSizePixels, AppIconSizePixels, true)
                   ?? throw new InvalidOperationException("Android could not scale the app icon.");

        var bitmap = Bitmap.CreateBitmap(
                         AppIconSizePixels,
                         AppIconSizePixels,
                         Bitmap.Config.Argb8888 ??
                         throw new InvalidOperationException("ARGB8888 bitmap config is unavailable."))
                     ?? throw new InvalidOperationException("Android could not allocate the app icon bitmap.");

        using var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
        drawable.Draw(canvas);
        return bitmap;
    }

    private static string? GetIconCacheDirectory(Context context)
    {
        return context.CacheDir?.AbsolutePath is { Length: > 0 } cacheRoot
            ? Path.Combine(cacheRoot, IconCacheDirectoryName)
            : null;
    }

    private static string? GetOrCreateIconCacheDirectory(Context context)
    {
        var directory = GetIconCacheDirectory(context);
        if (directory is null) return null;

        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryGetPackageIdentity(
        PackageManager packageManager,
        string packageName,
        out PackageIdentity identity)
    {
        try
        {
            var packageInfo =
                TryGetPackageInfo(packageManager, packageName, AndroidSystemApi.GetInstalledApplicationFlags())
                ?? packageManager.GetPackageInfo(packageName, 0);
            if (packageInfo is null)
            {
                identity = default;
                return false;
            }

            identity = new PackageIdentity(packageInfo.LongVersionCode);
            return true;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            identity = default;
            return false;
        }
    }

    private static bool TryGetPackageInventoryDetails(
        Context context,
        PackageManager packageManager,
        string packageName,
        out PackageIdentity identity,
        out AppPermissionRiskAnalysis permissionRisk)
    {
        try
        {
            var packageInfo =
                TryGetPackageInfo(
                    packageManager,
                    packageName,
                    AndroidSystemApi.GetInstalledApplicationFlags()
                    | PackageInfoFlags.Permissions
                    | PackageInfoFlags.Services)
                ?? packageManager.GetPackageInfo(packageName, PackageInfoFlags.Permissions | PackageInfoFlags.Services);
            if (packageInfo is null)
            {
                Log.Debug(LogTag, $"Package inventory details unavailable. package={packageName}, reason=PackageInfoNull.");
                identity = default;
                permissionRisk = AppPermissionRiskAnalysis.Safe;
                return false;
            }

            identity = new PackageIdentity(packageInfo.LongVersionCode);
            var appInfo = packageInfo.ApplicationInfo;
            permissionRisk = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
                packageInfo.RequestedPermissions,
                (int)Build.VERSION.SdkInt,
                appInfo is { } ? (int)appInfo.TargetSdkVersion : 0,
                GetForegroundServiceTypes(packageInfo),
                GetServicePermissions(packageInfo),
                GetGrantedPermissions(packageInfo, packageName),
                GetDeniedPermissions(packageInfo, packageName),
                IsAccessibilityServiceEnabled(context, packageName),
                IsNotificationListenerEnabled(context, packageName),
                IsAppOpAllowed(context, appInfo, SystemAlertWindowOp),
                IsAppOpAllowed(context, appInfo, UsageStatsOp),
                IsAppOpAllowedOrUnknown(context, appInfo, CameraOp),
                IsAppOpAllowedOrUnknown(context, appInfo, MicrophoneOp),
                IsAppOpAllowedOrUnknown(context, appInfo, FineLocationOp),
                IsAppOpAllowedOrUnknown(context, appInfo, CoarseLocationOp)));
            return true;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            Log.Debug(
                LogTag,
                $"Package inventory details unavailable. package={packageName}, error={exception.GetType().Name}.");
            identity = default;
            permissionRisk = AppPermissionRiskAnalysis.Safe;
            return false;
        }
    }

    private static string[] GetForegroundServiceTypes(PackageInfo packageInfo)
    {
        if (packageInfo.Services is not { Count: > 0 } services) return [];

        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            AddForegroundServiceType(types, service.ForegroundServiceType, ForegroundService.TypeCamera, "camera");
            AddForegroundServiceType(types, service.ForegroundServiceType, ForegroundService.TypeLocation, "location");
            AddForegroundServiceType(
                types,
                service.ForegroundServiceType,
                ForegroundService.TypeMediaProjection,
                "mediaProjection");
            AddForegroundServiceType(
                types,
                service.ForegroundServiceType,
                ForegroundService.TypeMicrophone,
                "microphone");
        }

        return types.ToArray();
    }

    private static void AddForegroundServiceType(
        HashSet<string> types,
        ForegroundService serviceTypes,
        ForegroundService type,
        string name)
    {
        if ((serviceTypes & type) != 0) types.Add(name);
    }

    private static string[] GetServicePermissions(PackageInfo packageInfo)
    {
        if (packageInfo.Services is not { Count: > 0 } services) return [];

        return services
            .Select(service => service.Permission)
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray()!;
    }

    private static string[] GetGrantedPermissions(PackageInfo packageInfo, string packageName)
    {
        return GetPermissionsByGrantState(packageInfo, packageName, true);
    }

    private static string[] GetDeniedPermissions(PackageInfo packageInfo, string packageName)
    {
        return GetPermissionsByGrantState(packageInfo, packageName, false);
    }

    private static string[] GetPermissionsByGrantState(PackageInfo packageInfo, string packageName, bool granted)
    {
        if (packageInfo.RequestedPermissions is not { Count: > 0 } permissions)
        {
            return [];
        }

        if (packageInfo.RequestedPermissionsFlags is not { Count: > 0 } flags)
        {
            Log.Debug(LogTag, $"Runtime permission grant flags unavailable. package={packageName}.");
            return [];
        }

        var count = Math.Min(permissions.Count, flags.Count);
        if (permissions.Count != flags.Count)
            Log.Debug(
                LogTag,
                $"Runtime permission flag count mismatch. package={packageName}, permissions={permissions.Count}, flags={flags.Count}.");

        var result = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var permission = permissions[index];
            if (string.IsNullOrWhiteSpace(permission)) continue;

            var isGranted = ((int)flags[index] & RequestedPermissionGrantedFlag) != 0;
            if (isGranted == granted) result.Add(permission);
        }

        return result.ToArray();
    }

    private static bool IsAccessibilityServiceEnabled(Context context, string packageName)
    {
        return SecureSettingContainsPackage(context, Settings.Secure.EnabledAccessibilityServices, packageName);
    }

    private static bool IsNotificationListenerEnabled(Context context, string packageName)
    {
        return SecureSettingContainsPackage(context, "enabled_notification_listeners", packageName);
    }

    private static bool SecureSettingContainsPackage(Context context, string settingName, string packageName)
    {
        try
        {
            var value = Settings.Secure.GetString(context.ContentResolver, settingName);
            return !string.IsNullOrWhiteSpace(value)
                   && value.Split(':', StringSplitOptions.RemoveEmptyEntries)
                       .Any(component => component.StartsWith(packageName + "/", StringComparison.Ordinal));
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    private static bool IsAppOpAllowed(Context context, ApplicationInfo? appInfo, string op)
    {
        return IsAppOpAllowedOrUnknown(context, appInfo, op) == true;
    }

    private static bool? IsAppOpAllowedOrUnknown(Context context, ApplicationInfo? appInfo, string op)
    {
        if (appInfo is null)
        {
            Log.Debug(LogTag, $"AppOps check skipped because ApplicationInfo is unavailable. op={op}.");
            return null;
        }

        try
        {
            if (context.GetSystemService(Context.AppOpsService) is not AppOpsManager appOpsManager)
            {
                Log.Debug(LogTag, $"AppOps service unavailable. op={op}.");
                return null;
            }

            if (appInfo.PackageName is not { Length: > 0 } packageName)
            {
                Log.Debug(LogTag, $"AppOps check skipped because package name is unavailable. op={op}.");
                return null;
            }

            var mode = appOpsManager.CheckOpNoThrow(op, appInfo.Uid, packageName);
            return mode == AppOpsManagerMode.Allowed
                ? true
                : mode is AppOpsManagerMode.Ignored or AppOpsManagerMode.Errored
                    ? false
                    : null;
        }
        catch (Exception exception) when (exception is Java.Lang.SecurityException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            Log.Debug(
                LogTag,
                $"AppOps check failed. package={appInfo.PackageName ?? "<null>"}, op={op}, error={exception.GetType().Name}.");
            return null;
        }
    }

    private static PackageInfo? TryGetPackageInfo(
        PackageManager packageManager,
        string packageName,
        PackageInfoFlags flags)
    {
        try
        {
            return packageManager.GetPackageInfo(packageName, flags);
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static string GetIconCacheKey(string packageName, PackageIdentity identity)
    {
        var packageHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packageName))).Substring(0, 16);
        return $"{packageHash}.{identity.VersionCode}";
    }

    private readonly record struct PackageIdentity(long VersionCode);

    private sealed record AppIconCacheEntry(PackageIdentity Identity, byte[]? IconPng);
}
