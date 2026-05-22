using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Platform;
using Agnosia.Models;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Gateways;

public static class AndroidProfileCommandGateway
{
    private const string LogTag = "AgnosiaProfileCommand";
    public const string ExtraTrigger = AndroidCommandContract.ExtraTrigger;
    private static readonly TimeSpan ProfilePingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BooleanQueryCacheTtl = TimeSpan.FromSeconds(5);
    private static readonly Lock BooleanQueryCacheSync = new();
    private static readonly Dictionary<string, CachedBooleanQuery> BooleanQueryCache = [];

    internal static async Task<bool> CanReachWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        return (await CheckWorkProfileOwnerAsync(commandRunner, cancellationToken)).Kind
               == WorkProfileOwnerCheckKind.AppIsProfileOwner;
    }

    internal static async Task<WorkProfileOwnerCheckResult> CheckWorkProfileOwnerAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = commandRunner.CurrentActivity;
        if (string.IsNullOrWhiteSpace(AuthenticationUtility.GetExistingKey()))
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.AuthenticationKeyMissing,
                "authKey=missing");

        if (!AgnosiaUtilities.HasWorkProfileTarget(activity))
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.TargetUnavailable,
                "crossProfileTarget=missing");

        using var pingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pingCancellation.CancelAfter(ProfilePingTimeout);
        try
        {
            var result = await commandRunner.StartActivityForResultAsync(
                new Intent(AgnosiaActions.ProfilePing),
                true,
                pingCancellation.Token);
            return InterpretProfilePingResult(result);
        }
        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn(LogTag, "Timed out waiting for work-profile ping.");
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.Unreachable,
                $"profilePing=timeout:{ProfilePingTimeout.TotalMilliseconds:0}ms");
        }
    }

    internal static async Task<ProfileAppsQueryResult?> QueryAppsAsync(
        AndroidActivityCommandGateway commandRunner,
        ProfileKind profile,
        bool showAll,
        CancellationToken cancellationToken)
    {
        if (profile == ProfileKind.Personal)
            return await QueryLocalAppsAsync(commandRunner.CurrentActivity, showAll, cancellationToken)
                .ConfigureAwait(false);

        var intent = new Intent(AgnosiaActions.QueryApps);
        intent.PutExtra(AndroidCommandContract.ExtraShowAll, showAll);
        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            true,
            cancellationToken).ConfigureAwait(false);
        if (!TryGetResultData(result, out var data))
        {
            Log.Warn(LogTag, "Failed to query work apps through the profile activity command.");
            return null;
        }

        var apps = DeserializeResult<IReadOnlyList<AppServiceModel>>(
            data.GetStringExtra(AndroidCommandContract.ResultAppsJson),
            "work apps") ?? [];
        var interactionPackages =
            data.GetStringArrayExtra(AndroidCommandContract.ResultInteractionPackages) ?? [];
        return new ProfileAppsQueryResult(apps, interactionPackages);
    }

    internal static async Task<byte[]?> LoadAppIconAsync(
        AndroidActivityCommandGateway commandRunner,
        AppSnapshot app,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (app.Profile == ProfileKind.Personal)
            return await LoadLocalAppIconAsync(
                    commandRunner.CurrentActivity,
                    app.PackageName,
                    cancellationToken)
                .ConfigureAwait(false);

        var intent = new Intent(AgnosiaActions.QueryAppIcon);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, app.PackageName);
        var result = await commandRunner.StartActivityForResultAsync(
                intent,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        if (!TryGetResultData(result, out var data))
        {
            Log.Warn(LogTag, $"Failed to query work app icon for {app.PackageName}.");
            return null;
        }

        return data.GetByteArrayExtra(AndroidCommandContract.ResultIconPng);
    }

    internal static async Task<IReadOnlyDictionary<string, byte[]?>> LoadAppIconsAsync(
        AndroidActivityCommandGateway commandRunner,
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (apps.Count == 0) return new Dictionary<string, byte[]?>(StringComparer.Ordinal);

        var icons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var personalPackageNames = apps
            .Where(app => app.Profile == ProfileKind.Personal)
            .Select(app => app.PackageName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var workApps = apps
            .Where(app => app.Profile == ProfileKind.Work)
            .ToArray();

        if (personalPackageNames.Length > 0)
        {
            var personalIcons = await LoadLocalAppIconsAsync(
                    commandRunner.CurrentActivity,
                    personalPackageNames,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var (packageName, iconPng) in personalIcons) icons[packageName] = iconPng;
        }

        if (workApps.Length > 0)
        {
            foreach (var app in workApps) icons[app.PackageName] = app.IconPng;
        }

        return icons;
    }

    internal static async Task<IReadOnlyList<string>> QueryWorkCrossProfilePackagesAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.StartActivityForResultAsync(
            new Intent(AgnosiaActions.QueryCrossProfilePackages),
            true,
            cancellationToken);
        if (!TryGetResultData(result, out var data))
        {
            Log.Warn(LogTag, "Failed to query work cross-profile packages through the profile activity command.");
            return [];
        }

        return data.GetStringArrayExtra(AndroidCommandContract.ResultInteractionPackages) ?? [];
    }

    internal static async Task<IReadOnlyList<AppLogEntry>> QueryWorkLogsAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.StartActivityForResultAsync(
            new Intent(AgnosiaActions.QueryLogs),
            true,
            cancellationToken);
        if (!TryGetResultData(result, out var data))
        {
            Log.Warn(LogTag, "Failed to query work logs through the profile activity command.");
            return [];
        }

        return DeserializeResult<IReadOnlyList<AppLogEntry>>(
            data.GetStringExtra(AndroidCommandContract.ResultLogsJson),
            "work logs") ?? [];
    }

    internal static Task<bool> QueryWorkUsageStatsAccessAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        return QueryWorkBooleanAsync(
            commandRunner,
            AgnosiaActions.QueryUsageStatsAccess,
            AndroidCommandContract.ResultUsageStatsAccess,
            cancellationToken);
    }

    internal static Task<bool> QueryWorkPackageInstallAccessAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        return QueryWorkBooleanAsync(
            commandRunner,
            AgnosiaActions.QueryPackageInstallAccess,
            AndroidCommandContract.ResultPackageInstallAccess,
            cancellationToken);
    }

    internal static Task<OperationResult> EnableSystemAppInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = CreateSystemPackageIntent(AgnosiaActions.InstallPackage, packageName);
        return RunWorkPackageOperationAsync(commandRunner, intent, successMessage, cancellationToken);
    }

    internal static Task<OperationResult> HideSystemAppInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = CreateSystemPackageIntent(AgnosiaActions.UninstallPackage, packageName);
        return RunWorkPackageOperationAsync(commandRunner, intent, successMessage, cancellationToken);
    }

    internal static Task<OperationResult> SetPackageHiddenInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        bool hidden,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = new Intent(hidden ? AgnosiaActions.FreezePackage : AgnosiaActions.UnfreezePackage);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, packageName);
        return RunWorkPackageOperationAsync(commandRunner, intent, successMessage, cancellationToken);
    }

    internal static async Task<OperationResult> RevokeRuntimePermissionsInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        var intent = new Intent(AgnosiaActions.RevokeRuntimePermissions);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, packageName);
        intent.PutExtra(AndroidCommandContract.ExtraPermissions, permissions.ToArray());

        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            true,
            cancellationToken);
        return AndroidActivityResultApi.ToVoidOperationResult(
            result,
            $"Runtime-разрешения отозваны: {permissions.Count}.");
    }

    internal static async Task<OperationResult> SetCrossProfileInteractionAsync(
        AndroidActivityCommandGateway commandRunner,
        string[] packages,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = new Intent(AgnosiaActions.SetCrossProfileInteraction);
        intent.PutExtra(AndroidCommandContract.ExtraPackages, packages);
        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            true,
            cancellationToken);
        if (result.ResultCode == Result.Canceled)
            return OperationResult.Failure(
                AndroidActivityResultApi.ExtractError(result)
                ?? "Android отклонил изменение межпрофильной политики.");

        return result.Data?.GetBooleanExtra(AndroidCommandContract.ResultToggleSuccess, false) == true
            ? OperationResult.Success(successMessage)
            : OperationResult.Failure("Android отклонил изменение межпрофильной политики.");
    }

    public static Task<OperationResult> SynchronizeBooleanToWorkProfileAsync(
        Context context,
        string name,
        bool value,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<OperationResult>(cancellationToken);

        var intent = new Intent(AgnosiaActions.SynchronizePreference);
        intent.PutExtra(AndroidCommandContract.ExtraPreferenceName, name);
        intent.PutExtra(AndroidCommandContract.ExtraPreferenceBoolean, value);
        return Task.FromResult(StartOtherProfileActivity(
            context,
            intent,
            "OK",
            $"Android не смог синхронизировать настройку {name} с рабочим профилем."));
    }

    public static OperationResult FreezePackageInWorkProfile(
        Context context,
        string packageName,
        string successMessage)
    {
        var intent = new Intent(AgnosiaActions.FreezePackage);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, packageName);
        return StartOtherProfileActivity(
            context,
            intent,
            successMessage,
            $"Android не смог скрыть {packageName} в рабочем профиле.");
    }

    public static OperationResult NotifyParentWorkAppFrozen(Context context, string trigger)
    {
        var intent = new Intent(AgnosiaActions.WorkAppFrozen);
        intent.PutExtra(AndroidCommandContract.ExtraTrigger, trigger);
        return StartOtherProfileActivity(
            context,
            intent,
            "Основной профиль получил событие заморозки приложения.",
            "Android не смог передать событие заморозки приложения в основной профиль.");
    }

    private static async Task<bool> QueryWorkBooleanAsync(
        AndroidActivityCommandGateway commandRunner,
        string action,
        string resultExtra,
        CancellationToken cancellationToken)
    {
        if (TryGetCachedBooleanQuery(action, out var cachedValue)) return cachedValue;

        var result = await commandRunner.StartActivityForResultAsync(
            new Intent(action),
            true,
            cancellationToken);
        var value = result.ResultCode == Result.Ok
                    && result.Data?.GetBooleanExtra(resultExtra, false) == true;
        SetCachedBooleanQuery(action, value);
        return value;
    }

    private static async Task<OperationResult> RunWorkPackageOperationAsync(
        AndroidActivityCommandGateway commandRunner,
        Intent intent,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            true,
            cancellationToken);
        return AndroidActivityResultApi.ToPackageOperationResult(result, successMessage);
    }

    private static Task<ProfileAppsQueryResult?> QueryLocalAppsAsync(
        Context context,
        bool showAll,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.PackageManager is not { } packageManager)
            {
                Log.Warn(LogTag, "PackageManager unavailable; could not query local apps.");
                return null;
            }

            var apps = AndroidAppInventoryApi.QueryInstalledApps(
                context,
                packageManager,
                AndroidSystemApi.GetDevicePolicyManager(context),
                null,
                showAll,
                cancellationToken);
            return new ProfileAppsQueryResult(apps, []);
        }, cancellationToken);
    }

    private static Task<byte[]?> LoadLocalAppIconAsync(
        Context context,
        string packageName,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return context.PackageManager is { } packageManager
                ? AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(context, packageManager, packageName)
                : null;
        }, cancellationToken);
    }

    private static Task<IReadOnlyDictionary<string, byte[]?>> LoadLocalAppIconsAsync(
        Context context,
        IReadOnlyList<string> packageNames,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyDictionary<string, byte[]?>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var icons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            if (context.PackageManager is not { } packageManager)
            {
                foreach (var packageName in packageNames) icons[packageName] = null;

                return icons;
            }

            foreach (var packageName in packageNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                icons[packageName] = AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(
                    context,
                    packageManager,
                    packageName);
            }

            return icons;
        }, cancellationToken);
    }

    private static Intent CreateSystemPackageIntent(string action, string packageName)
    {
        var intent = new Intent(action);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, packageName);
        intent.PutExtra(AndroidCommandContract.ExtraIsSystem, true);
        return intent;
    }

    private static bool TryGetResultData(AndroidActivityResult result, out Intent data)
    {
        if (result.ResultCode == Result.Ok && result.Data is { } resultData)
        {
            data = resultData;
            return true;
        }

        data = null!;
        return false;
    }

    private static OperationResult StartOtherProfileActivity(
        Context context,
        Intent intent,
        string successMessage,
        string errorMessage)
    {
        try
        {
            AgnosiaRuntime.Initialize(context);
            intent.AddFlags(ActivityFlags.NewTask);
            intent.AddCategory(Intent.CategoryDefault);
            AgnosiaUtilities.TransferIntentToProfile(context, intent);
            Log.Debug(
                LogTag,
                $"Starting other-profile activity. action={intent.Action ?? "<none>"}, component={intent.Component?.PackageName ?? "<none>"}/{intent.Component?.ClassName ?? "<none>"}.");
            StartActivityOnMainThread(context, intent);
            return OperationResult.Success(successMessage);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"{errorMessage} Details: {exception}");
            return OperationResult.Failure(errorMessage);
        }
    }

    private static void StartActivityOnMainThread(Context context, Intent intent)
    {
        if (Looper.MainLooper?.IsCurrentThread == true)
        {
            context.StartActivity(intent);
            return;
        }

        var mainLooper = Looper.MainLooper
                         ?? throw new InvalidOperationException("Android main looper is unavailable.");
        new Handler(mainLooper).Post(() =>
        {
            try
            {
                context.StartActivity(intent);
            }
            catch (Exception exception)
            {
                Log.Warn(
                    LogTag,
                    $"Failed to start other-profile activity on main thread. action={intent.Action ?? "<none>"}; error={exception.Message}");
            }
        });
    }

    private static T? DeserializeResult<T>(string? raw, string description)
    {
        if (string.IsNullOrWhiteSpace(raw)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to deserialize {description}: {exception.Message}");
            return default;
        }
    }

    private static bool TryGetCachedBooleanQuery(string action, out bool value)
    {
        lock (BooleanQueryCacheSync)
        {
            if (BooleanQueryCache.TryGetValue(action, out var cached)
                && DateTimeOffset.UtcNow - cached.CachedAt <= BooleanQueryCacheTtl)
            {
                value = cached.Value;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static void SetCachedBooleanQuery(string action, bool value)
    {
        lock (BooleanQueryCacheSync)
        {
            BooleanQueryCache[action] = new CachedBooleanQuery(value, DateTimeOffset.UtcNow);
        }
    }

    private sealed record CachedBooleanQuery(bool Value, DateTimeOffset CachedAt);

    private static WorkProfileOwnerCheckResult InterpretProfilePingResult(AndroidActivityResult result)
    {
        if (result.Data?.GetBooleanExtra(AndroidCommandContract.ResultProfileOwnerCheckPerformed, false) == true)
        {
            if (!AuthenticationUtility.CheckIntent(result.Data))
                return new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.Unreachable,
                    "profilePing=unsignedOwnerCheck");

            var isProfileOwner = result.Data.GetBooleanExtra(AndroidCommandContract.ResultIsProfileOwner, false);
            return isProfileOwner
                ? new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppIsProfileOwner,
                    "inProfileOwnerCheck=true")
                : new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppInstalledButNotOwner,
                    "inProfileOwnerCheck=false");
        }

        var error = AndroidActivityResultApi.ExtractError(result);
        return new WorkProfileOwnerCheckResult(
            WorkProfileOwnerCheckKind.Unreachable,
            string.IsNullOrWhiteSpace(error)
                ? $"profilePing=result:{result.ResultCode}"
                : $"profilePing=result:{result.ResultCode}; error={error}");
    }
}

internal sealed record ProfileAppsQueryResult(
    IReadOnlyList<AppServiceModel> Apps,
    IReadOnlyList<string> InteractionPackages);

internal enum WorkProfileOwnerCheckKind
{
    AuthenticationKeyMissing,
    TargetUnavailable,
    Unreachable,
    AppInstalledButNotOwner,
    AppIsProfileOwner
}

internal sealed record WorkProfileOwnerCheckResult(
    WorkProfileOwnerCheckKind Kind,
    string DiagnosticReason);
