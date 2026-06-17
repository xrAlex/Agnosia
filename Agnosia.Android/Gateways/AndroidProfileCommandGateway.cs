using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Platform;
using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Gateways;

public static class AndroidProfileCommandGateway
{
    private const string LogTag = "AgnosiaProfileCommand";
    public const string ExtraTrigger = AndroidCommandContract.ExtraTrigger;
    private static readonly TimeSpan ProfilePingTimeout = TimeSpan.FromSeconds(10);

    internal static async Task<bool> CanReachWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        return (await CheckWorkProfileOwnerAsync(commandRunner, cancellationToken).ConfigureAwait(false)).Kind
               == WorkProfileOwnerCheckKind.AppIsProfileOwner;
    }

    internal static async Task<WorkProfileOwnerCheckResult> CheckWorkProfileOwnerAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = commandRunner.CurrentActivity;
        if (string.IsNullOrWhiteSpace(AuthenticationUtility.GetExistingKey()))
            return await TryRecoverAuthenticationAsync(commandRunner, cancellationToken)
                .ConfigureAwait(false);

        if (!AgnosiaUtilities.HasWorkProfileTarget(activity))
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.TargetUnavailable,
                "crossProfileTarget=missing");

        if (AndroidQueryCache.Shared.TryGetSuccessfulOwnerCheck(out var cachedOwnerCheck))
            return cachedOwnerCheck;

        using var pingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pingCancellation.CancelAfter(ProfilePingTimeout);
        try
        {
            var result = await commandRunner.StartActivityForResultAsync(
                new Intent(AgnosiaActions.ProfilePing),
                true,
                pingCancellation.Token)
            .ConfigureAwait(false);
            var ownerCheck = InterpretProfilePingResult(result);
            AndroidQueryCache.Shared.StoreOwnerCheckIfSuccessful(ownerCheck);
            return ownerCheck;
        }
        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn(LogTag, "Timed out waiting for work-profile ping.");
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.Unreachable,
                $"profilePing=timeout:{ProfilePingTimeout.TotalMilliseconds:0}ms");
        }
    }

    private static async Task<WorkProfileOwnerCheckResult> TryRecoverAuthenticationAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        AndroidQueryCache.Shared.ClearOwnerCheck();
        var activity = commandRunner.CurrentActivity;
        if (!AgnosiaUtilities.HasWorkProfileTarget(activity))
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.TargetUnavailable,
                "authKey=missing; crossProfileTarget=missing");

        var replacementAuthKey = AuthenticationUtility.CreateAndStoreKey();
        using var pingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pingCancellation.CancelAfter(ProfilePingTimeout);

        try
        {
            var intent = new Intent(AgnosiaActions.RecoverAuthentication);
            intent.PutExtra(AndroidCommandContract.ExtraReplacementAuthKey, replacementAuthKey);
            var result = await commandRunner.StartUnsignedWorkProfileActivityForResultAsync(
                    intent,
                    pingCancellation.Token)
                .ConfigureAwait(false);
            var ownerCheck = InterpretProfilePingResult(result);
            if (ownerCheck.Kind == WorkProfileOwnerCheckKind.AppIsProfileOwner)
                return ownerCheck with { DiagnosticReason = "authKey=recovered; " + ownerCheck.DiagnosticReason };

            AuthenticationUtility.Reset();
            AndroidQueryCache.Shared.ClearOwnerCheck();
            return ownerCheck with { DiagnosticReason = "authKey=recoveryFailed; " + ownerCheck.DiagnosticReason };
        }
        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AuthenticationUtility.Reset();
            AndroidQueryCache.Shared.ClearOwnerCheck();
            Log.Warn(LogTag, "Timed out waiting for work-profile authentication recovery.");
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.AuthenticationKeyMissing,
                $"authKey=recoveryTimeout:{ProfilePingTimeout.TotalMilliseconds:0}ms");
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

        return await AndroidProfileAppsPager.QueryWorkAppsPagedAsync(commandRunner, showAll, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<byte[]?> LoadAppIconAsync(
        AndroidActivityCommandGateway commandRunner,
        AppSnapshot app,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (app.IsSystem) return null;

        if (app.Profile == ProfileKind.Personal)
            return await LoadLocalAppIconAsync(
                    commandRunner.CurrentActivity,
                    app.PackageName,
                    cancellationToken)
                .ConfigureAwait(false);

        var intent = new Intent(AgnosiaActions.QueryAppIcon);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, app.PackageName);
        var data = await AndroidProfileCommandTransport.StartForDataAsync(
                commandRunner,
                intent,
                $"Failed to query work app icon for {app.PackageName}.",
                cancellationToken)
            .ConfigureAwait(false);
        if (data is null) return null;

        return data.GetByteArrayExtra(AndroidCommandContract.ResultIconPng);
    }

    internal static async Task<IReadOnlyDictionary<AppItemKey, byte[]?>> LoadAppIconsAsync(
        AndroidActivityCommandGateway commandRunner,
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (apps.Count == 0) return new Dictionary<AppItemKey, byte[]?>();

        var icons = new Dictionary<AppItemKey, byte[]?>();
        var loadablePersonalPackageNames = apps
            .Where(app => app is { Profile: ProfileKind.Personal, IsSystem: false })
            .Select(app => app.PackageName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var workPackageNames = apps
            .Where(app => app.Profile == ProfileKind.Work)
            .Select(app => app.PackageName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var app in apps.Where(static app => app.IsSystem))
            icons[AppItemKey.FromSnapshot(app)] = null;

        if (loadablePersonalPackageNames.Length > 0)
        {
            var personalIcons = await LoadLocalAppIconsAsync(
                    commandRunner.CurrentActivity,
                    loadablePersonalPackageNames,
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var (packageName, iconPng) in personalIcons)
                icons[new AppItemKey(ProfileKind.Personal, packageName)] = iconPng;
        }

        if (workPackageNames.Length > 0)
        {
            var loadableWorkPackageNames = apps
                .Where(app => app is { Profile: ProfileKind.Work, IsSystem: false })
                .Select(app => app.PackageName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var packageName in workPackageNames)
                icons[new AppItemKey(ProfileKind.Work, packageName)] = null;

            if (loadableWorkPackageNames.Length > 0)
            {
                var intent = new Intent(AgnosiaActions.QueryAppIcons);
                intent.PutExtra(AndroidCommandContract.ExtraPackages, loadableWorkPackageNames);
                var data = await AndroidProfileCommandTransport.StartForDataAsync(
                        commandRunner,
                        intent,
                        "Failed to query work app icons through the profile activity command.",
                        cancellationToken)
                    .ConfigureAwait(false);
                var bundle = data?.GetBundleExtra(AndroidCommandContract.ResultIconsBundle);
                if (bundle is not null)
                    foreach (var packageName in loadableWorkPackageNames)
                        icons[new AppItemKey(ProfileKind.Work, packageName)] = bundle.GetByteArray(packageName);
            }
        }

        return icons;
    }

    internal static async Task<IReadOnlyList<string>> QueryWorkCrossProfilePackagesAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        var data = await AndroidProfileCommandTransport.StartForDataAsync(
                commandRunner,
                new Intent(AgnosiaActions.QueryCrossProfilePackages),
                "Failed to query work cross-profile packages through the profile activity command.",
                cancellationToken)
            .ConfigureAwait(false);
        if (data is null) return [];

        return AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(
            data.GetStringArrayExtra(AndroidCommandContract.ResultInteractionPackages) ?? []);
    }

    internal static async Task<IReadOnlyList<AppLogEntry>> QueryWorkLogsAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        var data = await AndroidProfileCommandTransport.StartForDataAsync(
                commandRunner,
                new Intent(AgnosiaActions.QueryLogs),
                "Failed to query work logs through the profile activity command.",
                cancellationToken)
            .ConfigureAwait(false);
        if (data is null) return [];

        return AndroidProfileCommandJson.DeserializeAppLogEntriesResult(
            data.GetStringExtra(AndroidCommandContract.ResultLogsJson),
            "work logs") ?? [];
    }

    internal static async Task<WorkProfilePermissionQueryResult> QueryWorkPermissionsAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        var data = await AndroidProfileCommandTransport.StartForDataAsync(
                commandRunner,
                new Intent(AgnosiaActions.QueryPermissions),
                "Failed to query work permissions through the profile activity command.",
                cancellationToken)
            .ConfigureAwait(false);
        if (data is null) return WorkProfilePermissionQueryResult.Empty;

        var result = new WorkProfilePermissionQueryResult(
            data.GetBooleanExtra(AndroidCommandContract.ResultUsageStatsAccess, false),
            data.GetBooleanExtra(AndroidCommandContract.ResultPackageInstallAccess, false),
            data.GetBooleanExtra(AndroidCommandContract.ResultAllFilesAccess, false));
        AndroidQueryCache.Shared.SetBoolean(AgnosiaActions.QueryUsageStatsAccess, result.UsageStatsAccess);
        AndroidQueryCache.Shared.SetBoolean(AgnosiaActions.QueryPackageInstallAccess, result.PackageInstallAccess);
        AndroidQueryCache.Shared.SetBoolean(AgnosiaActions.QueryAllFilesAccess, result.AllFilesAccess);
        return result;
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

    internal static Task<bool> QueryWorkAllFilesAccessAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        return QueryWorkBooleanAsync(
            commandRunner,
            AgnosiaActions.QueryAllFilesAccess,
            AndroidCommandContract.ResultAllFilesAccess,
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

    internal static async Task<OperationResult> UpdateAgnosiaInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        var sourceResolution = await ResolveInstalledPackageSourceAsync(
                activity.PackageManager,
                activity.PackageName,
                cancellationToken)
            .ConfigureAwait(false);
        if (!sourceResolution.Succeeded)
            return OperationResult.Failure("Android не смог определить APK Agnosia для обновления рабочего профиля.");

        var intent = new Intent(AgnosiaActions.InstallPackage);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, activity.PackageName);
        intent.PutExtra(AndroidCommandContract.ExtraIsSystem, false);
        intent.PutExtra(AndroidCommandContract.ExtraApk, sourceResolution.SourceDirectory);
        intent.PutExtra(AndroidCommandContract.ExtraSplitApks, sourceResolution.SplitApks);
        return await commandRunner.RunPackageOperationAsync(
                intent,
                true,
                cancellationToken,
                "Agnosia обновлена в рабочем профиле.")
            .ConfigureAwait(false);
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
                cancellationToken)
            .ConfigureAwait(false);
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
        intent.PutExtra(AndroidCommandContract.ExtraPackages,
            AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(packages));
        var result = await commandRunner.StartActivityForResultAsync(
                intent,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ResultCode == Result.Canceled)
            return OperationResult.Failure(
                AndroidActivityResultApi.ExtractError(result)
                ?? "Android отклонил изменение межпрофильной политики.");

        return result.Data?.GetBooleanExtra(AndroidCommandContract.ResultToggleSuccess, false) == true
            ? OperationResult.Success(successMessage)
            : OperationResult.Failure("Android отклонил изменение межпрофильной политики.");
    }

    internal static async Task<OperationResult> SetLockdownEnabledAsync(
        AndroidActivityCommandGateway commandRunner,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var intent = new Intent(AgnosiaActions.SetLockdownEnabled);
        intent.PutExtra(AndroidCommandContract.ExtraPreferenceBoolean, enabled);
        var result = await commandRunner.StartActivityForResultAsync(
                intent,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        return AndroidActivityResultApi.ToVoidOperationResult(
            result,
            enabled ? "Lockdown включён." : "Lockdown выключен.");
    }

    internal static async Task<OperationResult> SetLockdownInternetAccessAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        bool blocked,
        CancellationToken cancellationToken)
    {
        var intent = new Intent(AgnosiaActions.SetLockdownInternetAccess);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, packageName);
        intent.PutExtra(AndroidCommandContract.ExtraInternetBlocked, blocked);
        var result = await commandRunner.StartActivityForResultAsync(
                intent,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        return AndroidActivityResultApi.ToVoidOperationResult(
            result,
            blocked ? "Интернет приложения заблокирован." : "Интернет приложения разблокирован.");
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
        if (AndroidQueryCache.Shared.TryGetBoolean(action, out var cachedValue)) return cachedValue;

        var result = await commandRunner.StartActivityForResultAsync(
                new Intent(action),
                true,
                cancellationToken)
            .ConfigureAwait(false);
        var value = result.ResultCode == Result.Ok
                    && result.Data?.GetBooleanExtra(resultExtra, false) == true;
        AndroidQueryCache.Shared.SetBoolean(action, value);
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
                cancellationToken)
            .ConfigureAwait(false);
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
                cancellationToken: cancellationToken);
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

    private static Task<PackageSourceResolution> ResolveInstalledPackageSourceAsync(
        PackageManager? packageManager,
        string? packageName,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AndroidPackageApi.TryResolveInstalledPackageSource(
                packageManager,
                packageName,
                out var sourceDirectory,
                out var splitApks)
                ? new PackageSourceResolution(true, sourceDirectory, splitApks)
                : new PackageSourceResolution(false, null, []);
        }, cancellationToken);
    }

    private static Intent CreateSystemPackageIntent(string action, string packageName)
    {
        var intent = new Intent(action);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, packageName);
        intent.PutExtra(AndroidCommandContract.ExtraIsSystem, true);
        return intent;
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

    private static WorkProfileOwnerCheckResult InterpretProfilePingResult(AndroidActivityResult result)
    {
        if (result.Data?.GetBooleanExtra(AndroidCommandContract.ResultProfileOwnerCheckPerformed, false) == true)
        {
            if (!AuthenticationUtility.CheckIntent(result.Data))
                return new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.Unreachable,
                    "profilePing=unsignedOwnerCheck");

            var isProfileOwner = result.Data.GetBooleanExtra(AndroidCommandContract.ResultIsProfileOwner, false);
            var appVersionCode = result.Data.GetLongExtra(AndroidCommandContract.ResultAppVersionCode, 0);
            var appVersionName = result.Data.GetStringExtra(AndroidCommandContract.ResultAppVersionName);
            return isProfileOwner
                ? new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppIsProfileOwner,
                    "inProfileOwnerCheck=true",
                    appVersionCode,
                    appVersionName)
                : new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppInstalledButNotOwner,
                    "inProfileOwnerCheck=false",
                    appVersionCode,
                    appVersionName);
        }

        var error = AndroidActivityResultApi.ExtractError(result);
        return new WorkProfileOwnerCheckResult(
            WorkProfileOwnerCheckKind.Unreachable,
            string.IsNullOrWhiteSpace(error)
                ? $"profilePing=result:{result.ResultCode}"
                : $"profilePing=result:{result.ResultCode}; error={error}");
    }

    private sealed record PackageSourceResolution(
        bool Succeeded,
        string? SourceDirectory,
        string[] SplitApks);
}

internal sealed record ProfileAppsQueryResult(
    IReadOnlyList<AppServiceModel> Apps,
    IReadOnlyList<string> InteractionPackages);

internal sealed record WorkProfilePermissionQueryResult(
    bool UsageStatsAccess,
    bool PackageInstallAccess,
    bool AllFilesAccess)
{
    public static WorkProfilePermissionQueryResult Empty { get; } = new(false, false, false);
}
