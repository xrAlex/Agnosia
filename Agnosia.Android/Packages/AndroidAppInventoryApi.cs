using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Packages;

public static class AndroidAppInventoryApi
{
    private const string LogTag = "AndroidAppInventory";
    private const int RequestedPermissionGrantedFlag = 2;
    private const string CameraOp = "android:camera";
    private const string FineLocationOp = "android:fine_location";
    private const string CoarseLocationOp = "android:coarse_location";
    private const string MicrophoneOp = "android:record_audio";
    private const string UsageStatsOp = "android:get_usage_stats";
    private const string SystemAlertWindowOp = "android:system_alert_window";
    public static List<AppServiceModel> QueryInstalledApps(
        Context context,
        PackageManager packageManager,
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        bool showAll,
        CancellationToken cancellationToken = default,
        AppInventoryQueryOptions? options = null)
    {
        options ??= AppInventoryQueryOptions.Full;
        var isRiskEngineEnabled = LocalStorageManager.Instance.GetBoolean(StorageKeys.RiskEngineEnabled, true);
        var apps = packageManager.GetInstalledApplications(AndroidSystemApi.GetInstalledApplicationFlags());
        var models = new List<AppServiceModel>(apps.Count);
        var installedPackageNames = new HashSet<string>(StringComparer.Ordinal);
        var specialAccess = isRiskEngineEnabled ? ReadSpecialAccessSnapshot(context) : SpecialAccessSnapshot.Empty;
        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(app.PackageName)) installedPackageNames.Add(app.PackageName);

            if (TryCreateModel(
                    context,
                    packageManager,
                    policyManager,
                    admin,
                    app,
                    showAll,
                    specialAccess,
                    isRiskEngineEnabled,
                    options) is { } model)
                models.Add(model);
        }

        AndroidAppIconResolver.PruneMemoryIconCache(installedPackageNames);
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
            if (AndroidWorkProfilePackageClassifier.IsSystemApp(app)) return null;

            return AndroidAppIconResolver.ResolveAppIconPng(
                context,
                packageManager,
                app,
                packageName,
                identity,
                cancellationToken);
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
            var app = TryGetApplicationInfo(packageManager, packageName,
                AndroidSystemApi.GetInstalledApplicationFlags());
            if (app is not null && AndroidWorkProfilePackageClassifier.IsSystemApp(app)) return null;

            return TryGetPackageIdentity(packageManager, packageName, out var identity)
                   && AndroidAppIconResolver.TryLoadCachedAppIconPng(context, packageName, identity, out var cachedIcon)
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
        bool showAll,
        SpecialAccessSnapshot specialAccess,
        bool isRiskEngineEnabled,
        AppInventoryQueryOptions options)
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
                permissionRisk,
                permissionRiskAvailable: true,
                loadIcon: false,
                includeApkPaths: options.IncludeSystemApkPaths);
        }

        PackageIdentity packageIdentity;
        if (isRiskEngineEnabled)
        {
            if (!TryGetPackageInventoryDetails(
                    context,
                    packageManager,
                    packageName,
                    specialAccess,
                    out packageIdentity,
                    out permissionRisk)) return null;
        }
        else if (!TryGetPackageIdentity(packageManager, packageName, out packageIdentity))
        {
            return null;
        }

        return CreateModel(
            context,
            packageManager,
            app,
            packageName,
            isSystem,
            isHidden,
            isInstalled,
            packageIdentity,
            permissionRisk,
            isRiskEngineEnabled,
            loadIcon: options.IncludeInlineIcons,
            includeApkPaths: true);
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
        AppPermissionRiskAnalysis permissionRisk,
        bool permissionRiskAvailable,
        bool loadIcon,
        bool includeApkPaths)
    {
        return new AppServiceModel
        {
            PackageName = packageName,
            Label = packageManager.GetApplicationLabel(app),
            SourceDirectory = includeApkPaths ? app.SourceDir : null,
            SplitApks = includeApkPaths ? app.SplitSourceDirs?.ToArray() ?? [] : [],
            IsSystem = isSystem,
            IsHidden = isHidden,
            CanLaunch = packageManager.GetLaunchIntentForPackage(packageName) is not null,
            IsInstalled = isInstalled,
            PermissionRiskAvailable = permissionRiskAvailable,
            PermissionRiskLevel = permissionRisk.Level,
            RiskyPermissions = permissionRisk.RiskyPermissions.ToArray(),
            MatchedPermissionRiskRuleIds = permissionRisk.MatchedRuleIds.ToArray(),
            PermissionRiskScore = permissionRisk.Score,
            PermissionRiskRawScore = permissionRisk.RawScore,
            PermissionRiskConfidence = permissionRisk.Confidence,
            PermissionRiskScoreBreakdown = permissionRisk.ScoreBreakdown,
            ManifestPermissions = permissionRisk.ManifestPermissions.ToArray(),
            RuntimePermissions = permissionRisk.RuntimePermissions.ToArray(),
            IconPng = loadIcon
                ? AndroidAppIconResolver.TryLoadCachedAppIconPng(
                    context,
                    packageName,
                    packageIdentity,
                    out var cachedIcon)
                    ? cachedIcon
                    : AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(context, packageManager, packageName)
                : null
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
        SpecialAccessSnapshot specialAccess,
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
                specialAccess.HasAccessibilityService(packageName),
                specialAccess.HasNotificationListener(packageName),
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

        var result = new List<string>(services.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            var permission = service.Permission;
            if (string.IsNullOrWhiteSpace(permission) || !seen.Add(permission)) continue;

            result.Add(permission);
        }

        return result.Count == 0 ? [] : result.ToArray();
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

    private static SpecialAccessSnapshot ReadSpecialAccessSnapshot(Context context)
    {
        return new SpecialAccessSnapshot(
            ReadSecureComponentPackages(context, Settings.Secure.EnabledAccessibilityServices),
            ReadSecureComponentPackages(context, "enabled_notification_listeners"));
    }

    private static HashSet<string> ReadSecureComponentPackages(Context context, string settingName)
    {
        try
        {
            return ParseSecureComponentPackages(Settings.Secure.GetString(context.ContentResolver, settingName));
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static HashSet<string> ParseSecureComponentPackages(string? value)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return packages;

        var start = 0;
        while (start < value.Length)
        {
            var end = value.IndexOf(':', start);
            if (end < 0) end = value.Length;

            var slash = value.IndexOf('/', start, end - start);
            if (slash > start) packages.Add(value[start..slash]);

            start = end + 1;
        }

        return packages;
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

    private sealed record SpecialAccessSnapshot(
        HashSet<string> AccessibilityServicePackages,
        HashSet<string> NotificationListenerPackages)
    {
        public static SpecialAccessSnapshot Empty { get; } = new(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        public bool HasAccessibilityService(string packageName)
        {
            return AccessibilityServicePackages.Contains(packageName);
        }

        public bool HasNotificationListener(string packageName)
        {
            return NotificationListenerPackages.Contains(packageName);
        }
    }
}

public sealed record AppInventoryQueryOptions(
    bool IncludeInlineIcons,
    bool IncludeSystemApkPaths)
{
    public static AppInventoryQueryOptions Full { get; } = new(true, true);

    public static AppInventoryQueryOptions WorkList { get; } = new(false, false);
}
