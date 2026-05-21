using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Infrastructure;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Android.Shortcuts;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

[Activity(
    Name = "com.agnosia.app.DummyActivity",
    Theme = "@android:style/Theme.Translucent.NoTitleBar",
    Exported = true,
    ExcludeFromRecents = true,
    LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
[
    AgnosiaActions.FinalizeProvision,
    AgnosiaActions.ProfilePing,
    AgnosiaActions.QueryApps,
    AgnosiaActions.QueryAppIcon,
    AgnosiaActions.QueryAppIcons,
    AgnosiaActions.QueryLogs,
    AgnosiaActions.QueryCrossProfilePackages,
    AgnosiaActions.QueryUsageStatsAccess,
    AgnosiaActions.RequestUsageStatsAccess,
    AgnosiaActions.QueryPackageInstallAccess,
    AgnosiaActions.RequestPackageInstallAccess,
    AgnosiaActions.InstallPackage,
    AgnosiaActions.UninstallPackage,
    AgnosiaActions.FreezePackage,
    AgnosiaActions.UnfreezePackage,
    AgnosiaActions.PrepareHiddenShortcut,
    AgnosiaActions.CreateHiddenShortcut,
    AgnosiaActions.UnfreezeAndLaunch,
    AgnosiaActions.SetCrossProfileInteraction,
    AgnosiaActions.SynchronizePreference,
    AgnosiaActions.WorkAppFrozen,
    AgnosiaActions.PackageInstallerCallback
], Categories = [Intent.CategoryDefault])]
public sealed class DummyActivity : Activity
{
    private const string LogTag = "AgnosiaDummyActivity";
    private const int ProxyLaunchRequestCode = 7201;
    private static readonly TimeSpan PackageAvailabilityWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PackageAvailabilityRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HideAfterInstallRetryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HideAfterInstallRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly Type AdminReceiverType = typeof(AgnosiaDeviceAdminReceiver);

    private DevicePolicyManager? _policyManager;
    private readonly CancellationTokenSource _destroyCancellation = new();
    private bool _isProfileOwner;
    private bool _finishRequested;
    private AndroidAppLaunchResult? _pendingProxyLaunchResult;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AgnosiaRuntime.Initialize(this);

        _policyManager = AndroidSystemApi.GetDevicePolicyManager(this);
        _isProfileOwner = _policyManager?.IsProfileOwnerApp(PackageName) == true;
        if (_isProfileOwner)
        {
            AndroidStartup.EnforceWorkProfilePoliciesAndStartLockFreezeMonitor(
                this,
                string.Equals(Intent?.Action, AgnosiaActions.FinalizeProvision, StringComparison.Ordinal));
        }

        HandleAction();
    }

    protected override void OnResume()
    {
        base.OnResume();

        PackageInstallerCallbackCoordinator.RegisterActive(this);
        DeliverPendingPackageInstallerCallback();
    }

    protected override void OnPause()
    {
        PackageInstallerCallbackCoordinator.UnregisterActive(this);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _destroyCancellation.Cancel();
        PackageInstallerCallbackCoordinator.UnregisterActive(this);
        _destroyCancellation.Dispose();
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is null)
            return;

        Intent = intent;
        if (string.Equals(intent.Action, AgnosiaActions.PackageInstallerCallback, StringComparison.Ordinal))
        {
            HandlePackageInstallerCallback(intent);
            return;
        }

        HandleAction();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != ProxyLaunchRequestCode) return;

        var fallback = _pendingProxyLaunchResult
                       ?? AndroidAppLaunchResult.CommandReceived(null, null);
        _pendingProxyLaunchResult = null;

        if (AndroidAppLaunchResult.TryRead(data, out var launchResult))
        {
            launchResult.Log(LogTag);
            FinishWithResult(resultCode, data);
            return;
        }

        var failedResult = fallback.Fail(
            AndroidAppLaunchStage.CommandReceived,
            AndroidAppLaunchIssueKind.StartActivityException,
            "proxy_result_missing",
            "ProxyActivity не вернула результат попытки запуска приложения.");
        failedResult.Log(LogTag);
        FinishWithResult(Result.Canceled, failedResult.ToIntent());
    }

    private void HandleAction()
    {
        var action = Intent?.Action;
        if (string.IsNullOrWhiteSpace(action))
        {
            Finish();
            return;
        }

        if (string.Equals(action, AgnosiaActions.PackageInstallerCallback, StringComparison.Ordinal))
        {
            HandlePackageInstallerCallback(Intent);
            return;
        }

        Log.Debug(LogTag, $"Handling signed action={action}, isProfileOwner={_isProfileOwner}.");
        if (!AuthenticationUtility.CheckIntent(Intent)
            && !AuthenticationUtility.CheckWorkAppFrozenCallback(Intent))
        {
            Log.Warn(LogTag,
                $"Rejected signed action={action}: authentication check failed. isProfileOwner={_isProfileOwner}.");
            Finish();
            return;
        }

        try
        {
            switch (action)
            {
                case AgnosiaActions.ProfilePing:
                    var pingResult = new Intent();
                    pingResult.PutExtra(AndroidCommandContract.ResultProfileOwnerCheckPerformed, true);
                    pingResult.PutExtra(AndroidCommandContract.ResultIsProfileOwner, _isProfileOwner);
                    if (!_isProfileOwner)
                    {
                        pingResult.PutExtra(AndroidCommandContract.ResultError,
                            "Рабочий профиль не управляется Agnosia.");
                        AuthenticationUtility.SignIntent(pingResult);
                        FinishWithResult(Result.Canceled, pingResult);
                        break;
                    }

                    if (_isProfileOwner)
                    {
                        AndroidStartup.EnforceWorkProfilePoliciesAndStartLockFreezeMonitor(this);
                    }

                    AuthenticationUtility.SignIntent(pingResult);
                    FinishWithResult(Result.Ok, pingResult);
                    break;
                case AgnosiaActions.QueryApps:
                    RunAction(ActionQueryAppsAsync, "Android не смог получить список приложений.");
                    break;
                case AgnosiaActions.QueryAppIcon:
                    RunAction(ActionQueryAppIconAsync, "Android не смог получить иконку приложения.");
                    break;
                case AgnosiaActions.QueryAppIcons:
                    RunAction(ActionQueryAppIconsAsync, "Android не смог получить иконки приложений.");
                    break;
                case AgnosiaActions.QueryLogs:
                    ActionQueryLogs();
                    break;
                case AgnosiaActions.QueryCrossProfilePackages:
                    ActionQueryCrossProfilePackages();
                    break;
                case AgnosiaActions.QueryUsageStatsAccess:
                    ActionQueryUsageStatsAccess();
                    break;
                case AgnosiaActions.RequestUsageStatsAccess:
                    ActionRequestUsageStatsAccess();
                    break;
                case AgnosiaActions.QueryPackageInstallAccess:
                    ActionQueryPackageInstallAccess();
                    break;
                case AgnosiaActions.RequestPackageInstallAccess:
                    ActionRequestPackageInstallAccess();
                    break;
                case AgnosiaActions.InstallPackage:
                    ActionInstallPackage();
                    break;
                case AgnosiaActions.UninstallPackage:
                    ActionUninstallPackage();
                    break;
                case AgnosiaActions.PrepareHiddenShortcut:
                    RunAction(ActionPrepareHiddenShortcutAsync,
                        "Android не смог подготовить ярлык скрытого приложения.");
                    break;
                case AgnosiaActions.CreateHiddenShortcut:
                    ActionCreateHiddenShortcut();
                    break;
                case AgnosiaActions.FreezePackage:
                    ActionFreezePackage(true);
                    break;
                case AgnosiaActions.UnfreezePackage:
                    ActionFreezePackage(false);
                    break;
                case AgnosiaActions.UnfreezeAndLaunch:
                    ActionUnfreezeAndLaunch();
                    break;
                case AgnosiaActions.SetCrossProfileInteraction:
                    ActionSetCrossProfileInteraction();
                    break;
                case AgnosiaActions.SynchronizePreference:
                    ActionSynchronizePreference();
                    break;
                case AgnosiaActions.WorkAppFrozen:
                    RunAction(ActionWorkAppFrozenAsync, "Android не смог обработать событие заморозки приложения.");
                    break;
                case AgnosiaActions.FinalizeProvision:
                    ActionFinalizeProvision();
                    break;
                default:
                    Finish();
                    break;
            }
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to handle action {action}: {exception}");
            FinishWithError("Android не смог выполнить системное действие Agnosia.");
        }
    }

    private void RunAction(
        Func<CancellationToken, Task> action,
        string fallbackErrorMessage)
    {
        var cancellationToken = _destroyCancellation.Token;
        _ = RunActionAsync(action, fallbackErrorMessage, cancellationToken);
    }

    private async Task RunActionAsync(
        Func<CancellationToken, Task> action,
        string fallbackErrorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"{fallbackErrorMessage} Details: {exception}");
            FinishWithError(fallbackErrorMessage);
        }
    }

    private async Task ActionQueryAppsAsync(CancellationToken cancellationToken)
    {
        var intent = Intent;
        var packageManager = PackageManager;
        if (intent is null || packageManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        try
        {
            var showAll = intent.GetBooleanExtra("show_all", false);
            var policyManager = _policyManager;
            var admin = _isProfileOwner && policyManager is not null
                ? AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType)
                : null;
            var models = await Task.Run(() => AndroidAppInventoryApi.QueryInstalledApps(
                this,
                packageManager,
                policyManager,
                admin,
                showAll,
                cancellationToken), cancellationToken);

            var result = new Intent();
            result.PutExtra(AndroidCommandContract.ResultAppsJson, JsonSerializer.Serialize(models));

            if (admin is not null && policyManager is not null)
                result.PutExtra(AndroidCommandContract.ResultInteractionPackages,
                    AndroidPolicyApi.GetCrossProfilePackages(policyManager, admin));

            FinishWithResult(Result.Ok, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query apps: {exception}");
            FinishWithResult(Result.Canceled);
        }
    }

    private async Task ActionQueryAppIconAsync(CancellationToken cancellationToken)
    {
        var intent = Intent;
        var packageManager = PackageManager;
        var packageName = intent?.GetStringExtra("package");
        if (string.IsNullOrWhiteSpace(packageName) || packageManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        try
        {
            var iconPng = await Task.Run(() => AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(
                this,
                packageManager,
                packageName), cancellationToken);
            var result = new Intent();
            if (iconPng is { Length: > 0 }) result.PutExtra(AndroidCommandContract.ResultIconPng, iconPng);

            FinishWithResult(Result.Ok, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query app icon for {packageName}: {exception}");
            FinishWithResult(Result.Canceled);
        }
    }

    private async Task ActionQueryAppIconsAsync(CancellationToken cancellationToken)
    {
        var packageManager = PackageManager;
        var packageNames = Intent?.GetStringArrayExtra("packages") ?? [];
        if (packageNames.Length == 0 || packageManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        try
        {
            var icons = await Task.Run(() =>
            {
                var loadedIcons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
                foreach (var packageName in packageNames.Distinct(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    loadedIcons[packageName] = AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(
                        this,
                        packageManager,
                        packageName);
                }

                return loadedIcons;
            }, cancellationToken);

            var result = new Intent();
            var bundle = new Bundle();
            foreach (var (packageName, iconPng) in icons)
                if (iconPng is { Length: > 0 })
                    bundle.PutByteArray(packageName, iconPng);

            result.PutExtra(AndroidCommandContract.ResultIconsBundle, bundle);
            FinishWithResult(Result.Ok, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query app icons: {exception}");
            FinishWithResult(Result.Canceled);
        }
    }

    private void ActionQueryLogs()
    {
        var result = new Intent();
        result.PutExtra(
            AndroidCommandContract.ResultLogsJson,
            JsonSerializer.Serialize(AndroidAppLogArchive.Load(this)));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionQueryCrossProfilePackages()
    {
        if (!_isProfileOwner || _policyManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        var result = new Intent();
        result.PutExtra(
            AndroidCommandContract.ResultInteractionPackages,
            AndroidPolicyApi.GetCrossProfilePackages(
                _policyManager,
                AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType)));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionQueryUsageStatsAccess()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultUsageStatsAccess,
            AndroidUsageStatsAccessApi.HasAccess(this, LogTag));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionRequestUsageStatsAccess()
    {
        if (AndroidUsageStatsAccessApi.HasAccess(this, LogTag))
        {
            FinishWithSuccessMessage("Доступ к истории использования уже включен.");
            return;
        }

        if (AndroidUsageStatsAccessApi.TryOpenSettings(this, LogTag, FinishWithError))
            FinishWithSuccessMessage(
                "Откройте Agnosia в настройках Android и включите доступ к истории использования.");
    }

    private void ActionQueryPackageInstallAccess()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultPackageInstallAccess,
            AndroidPackageApi.CanRequestInstalls(this, LogTag));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionRequestPackageInstallAccess()
    {
        if (AndroidPackageApi.CanRequestInstalls(this, LogTag))
        {
            FinishWithSuccessMessage("Доступ к установке APK уже включен.");
            return;
        }

        if (AndroidPackageApi.TryOpenUnknownSourcesSettings(this, LogTag, FinishWithError))
            FinishWithSuccessMessage("Включите установку APK из Agnosia в рабочем профиле.");
    }

    private void ActionInstallPackage()
    {
        var intent = Intent;
        if (intent is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        var packageName = intent.GetStringExtra("package");
        var isSystem = intent.GetBooleanExtra("is_system", false);

        if (isSystem)
        {
            if (!_isProfileOwner || _policyManager is null || string.IsNullOrWhiteSpace(packageName))
            {
                FinishWithSystemAppError();
                return;
            }

            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
            if (!AndroidPolicyApi.TryEnableSystemApp(_policyManager, admin, packageName, LogTag, out var error))
            {
                FinishWithError(error ?? $"Android не смог включить системное приложение {packageName}.");
                return;
            }

            FinishWithResult(Result.Ok);
            return;
        }

        if (_isProfileOwner
            && _policyManager is not null
            && !string.IsNullOrWhiteSpace(packageName))
        {
            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
            if (AndroidPolicyApi.TryInstallExistingPackage(
                    _policyManager,
                    admin,
                    packageName,
                    LogTag,
                    out _))
            {
                FinishWithResult(Result.Ok);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(intent.GetStringExtra("apk")))
        {
            FinishWithError(
                "Android не смог установить приложение в рабочий профиль: пакет не найден в другом профиле, а APK недоступен для копирования.");
            return;
        }

        var callbackPendingIntent = AndroidPendingIntentApi.CreatePackageInstallerCallbackPendingIntent(
            this,
            typeof(PackageInstallerCallbackReceiver),
            AgnosiaActions.PackageInstallerCallback,
            packageName,
            AndroidCommandContract.PackageInstallerOperationInstall);
        if (!AndroidPackageApi.TryStartInstall(
                this,
                packageName,
                intent.GetStringExtra("apk"),
                intent.GetStringArrayExtra("split_apks"),
                callbackPendingIntent,
                LogTag,
                FinishWithError))
            FinishWithResult(Result.Canceled);
    }

    private void ActionUninstallPackage()
    {
        var intent = Intent;
        if (intent is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        var packageName = intent.GetStringExtra("package");
        var isSystem = intent.GetBooleanExtra("is_system", false);

        if (string.IsNullOrWhiteSpace(packageName))
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        if (isSystem && _isProfileOwner && _policyManager is not null)
        {
            if (!AndroidPolicyApi.TrySetApplicationHidden(
                    _policyManager,
                    AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType),
                    packageName,
                    true,
                    LogTag,
                    out var error))
            {
                FinishWithError(error ?? $"Android не смог скрыть {packageName}.");
                return;
            }

            FinishWithResult(Result.Ok);
            return;
        }

        var pendingIntent = AndroidPendingIntentApi.CreatePackageInstallerCallbackPendingIntent(
            this,
            typeof(PackageInstallerCallbackReceiver),
            AgnosiaActions.PackageInstallerCallback,
            packageName,
            AndroidCommandContract.PackageInstallerOperationUninstall);
        if (!AndroidPackageApi.TryStartUninstall(this, packageName, pendingIntent)) FinishWithResult(Result.Canceled);
    }

    private void ActionFreezePackage(bool hidden)
    {
        var packageName = Intent?.GetStringExtra("package");
        if (!_isProfileOwner || _policyManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            Log.Warn(LogTag,
                $"Freeze package command rejected. package={packageName ?? "<none>"}, hidden={hidden}, isProfileOwner={_isProfileOwner}, hasPolicyManager={_policyManager is not null}.");
            FinishWithResult(Result.Canceled);
            return;
        }

        var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
        if (!AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, hidden, LogTag,
                out var error))
        {
            FinishWithError(error ?? (hidden
                ? $"Android не смог скрыть {packageName}."
                : $"Android не смог восстановить {packageName}."));
            return;
        }

        FinishWithSuccessMessage(hidden
            ? "Приложение скрыто."
            : "Приложение снова доступно в рабочем профиле.");
    }

    private async Task ActionPrepareHiddenShortcutAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_isProfileOwner)
            {
                FinishWithError("Подготовка ярлыка скрытого приложения доступна только в рабочем профиле.");
                return;
            }

            var packageName = Intent?.GetStringExtra("package");
            if (string.IsNullOrWhiteSpace(packageName))
            {
                FinishWithError("Не указан пакет для ярлыка скрытого приложения.");
                return;
            }

            Log.Debug(LogTag, $"Starting hidden shortcut preparation for {packageName}.");
            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);

            if (!TryMakePackageVisibleForShortcutPreparation(admin, packageName, out var restoreHiddenState, out var visibilityError))
            {
                FinishWithError(visibilityError ?? $"Android не смог восстановить {packageName} для подготовки ярлыка.");
                return;
            }

            try
            {
                if (!await WaitForPackageAvailableAsync(packageName, cancellationToken))
                {
                    FinishWithError(
                        $"Android не видит {packageName} в рабочем профиле. Проверьте, что приложение установлено в рабочем профиле, и повторите действие.");
                    return;
                }

                HiddenAppShortcutBuildResult metadataResult;
                try
                {
                    metadataResult = await Task.Run(
                        () => HiddenAppShortcutManager.BuildMetadataAsync(
                            this,
                            packageName,
                            cancellationToken),
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Log.Error(LogTag, $"Failed to prepare hidden shortcut for {packageName}: {exception}");
                    FinishWithError($"Android не смог подготовить данные ярлыка для {packageName}.");
                    return;
                }

                if (!metadataResult.Succeeded)
                {
                    FinishWithError(metadataResult.Error);
                    return;
                }

                var hideError = await Task.Run(
                    () => TryHidePackageAfterInstallAsync(admin, packageName, cancellationToken),
                    cancellationToken);
                var preHideSucceeded = hideError is null;
                if (preHideSucceeded)
                {
                    restoreHiddenState = false;
                    Log.Info(LogTag, $"Installed hidden app {packageName} was frozen before shortcut creation.");
                }
                else
                {
                    Log.Warn(LogTag,
                        $"Continuing hidden shortcut creation after pre-hide failure. package={packageName}, error={hideError}");
                }

                var result = new Intent();
                HiddenAppShortcutManager.WriteMetadataToIntent(result, metadataResult.Metadata);
                result.PutExtra(AndroidCommandContract.ResultPreHideSucceeded, preHideSucceeded);
                if (!preHideSucceeded && !string.IsNullOrWhiteSpace(hideError))
                    result.PutExtra(AndroidCommandContract.ResultError, hideError);
                FinishWithResult(Result.Ok, result);
            }
            finally
            {
                if (restoreHiddenState)
                    RestoreHiddenStateAfterShortcutPreparation(admin, packageName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"ActionPrepareHiddenShortcut failed: {ex}");
            FinishWithError("Android не смог подготовить ярлык скрытого приложения.");
        }
    }

    private bool TryMakePackageVisibleForShortcutPreparation(
        ComponentName admin,
        string packageName,
        out bool restoreHiddenState,
        out string? error)
    {
        restoreHiddenState = false;
        error = null;

        if (_policyManager is null)
        {
            error = $"Android не смог проверить состояние {packageName} в рабочем профиле.";
            return false;
        }

        bool isHidden;
        try
        {
            isHidden = _policyManager.IsApplicationHidden(admin, packageName);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag,
                $"Could not read hidden state before hidden shortcut preparation. package={packageName}, exception={exception.GetType().FullName}: {exception.Message}");
            return true;
        }

        if (!isHidden) return true;

        Log.Info(LogTag,
            $"Package {packageName} is hidden before shortcut preparation; temporarily unhiding it to resolve metadata.");
        if (AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, false, LogTag, out error))
        {
            restoreHiddenState = true;
            return true;
        }

        error ??= $"Android не смог восстановить {packageName} для подготовки ярлыка.";
        return false;
    }

    private void RestoreHiddenStateAfterShortcutPreparation(ComponentName admin, string packageName)
    {
        if (_policyManager is null) return;

        Log.Info(LogTag,
            $"Restoring hidden state after incomplete shortcut preparation. package={packageName}.");
        if (!AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, true, LogTag, out var error))
            Log.Warn(LogTag,
                $"Failed to restore hidden state after incomplete shortcut preparation. package={packageName}, error={error ?? "<none>"}.");
    }

    private async Task<bool> WaitForPackageAvailableAsync(
        string packageName,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + PackageAvailabilityWaitTimeout;
        var attempt = 1;
        while (true)
        {
            if (IsPackageAvailable(packageName))
            {
                if (attempt > 1)
                    Log.Info(LogTag,
                        $"Package {packageName} became available for hidden shortcut preparation on attempt {attempt}.");

                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Log.Warn(LogTag,
                    $"Timed out waiting for package {packageName} to become available after install. timeoutMs={PackageAvailabilityWaitTimeout.TotalMilliseconds:0}, pollMs={PackageAvailabilityRetryDelay.TotalMilliseconds:0}.");
                return false;
            }

            await Task.Delay(PackageAvailabilityRetryDelay, cancellationToken);
            attempt++;
        }
    }

    private bool IsPackageAvailable(string packageName)
    {
        try
        {
            var packageInfo = PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.MatchDisabledComponents);
            var applicationInfo = packageInfo?.ApplicationInfo;
            var isInstalled = applicationInfo is not null
                              && (applicationInfo.Flags & ApplicationInfoFlags.Installed) != 0;
            if (!isInstalled)
                Log.Debug(LogTag,
                    $"Package is not installed yet. package={packageName}, hasPackageInfo={packageInfo is not null}, hasApplicationInfo={applicationInfo is not null}.");

            return isInstalled;
        }
        catch (PackageManager.NameNotFoundException)
        {
            Log.Debug(LogTag, $"Package is not visible yet. package={packageName}, reason=NameNotFound.");
            return false;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag,
                $"Failed to query package availability. package={packageName}, exception={exception.GetType().FullName}: {exception}");
            return false;
        }
    }

    private async Task<string?> TryHidePackageAfterInstallAsync(
        ComponentName admin,
        string packageName,
        CancellationToken cancellationToken)
    {
        if (_policyManager is null) return $"Android не смог скрыть {packageName} после установки.";

        var deadline = DateTimeOffset.UtcNow + HideAfterInstallRetryTimeout;
        var attempt = 1;
        while (true)
        {
            LogTechnicalHidePreflight(admin, packageName, attempt);
            if (AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, true, LogTag,
                    out var hideError))
            {
                if (attempt > 1)
                    Log.Info(LogTag, $"Package {packageName} was hidden after install on attempt {attempt}.");

                return null;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Log.Warn(LogTag,
                    $"Timed out hiding package {packageName} after install. lastError={hideError ?? "<none>"}.");
                return hideError ?? $"Android не смог скрыть {packageName} после установки.";
            }

            await Task.Delay(HideAfterInstallRetryDelay, cancellationToken);
            attempt++;
        }
    }

    private void LogTechnicalHidePreflight(ComponentName admin, string packageName, int attempt)
    {
        try
        {
            var isInstalled = IsPackageAvailable(packageName);
            bool? isHiddenBefore = null;
            try
            {
                isHiddenBefore = _policyManager?.IsApplicationHidden(admin, packageName);
            }
            catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
            {
                Log.Warn(LogTag,
                    $"PREPARE_HIDDEN_SHORTCUT hidden-state preflight failed. package={packageName}, attempt={attempt}, exception={exception.GetType().FullName}: {exception.Message}");
            }

            Log.Info(
                LogTag,
                $"PREPARE_HIDDEN_SHORTCUT hide preflight. package={packageName}, operation=hidePackage, attempt={attempt}, isInstalled={isInstalled}, isHiddenBefore={isHiddenBefore?.ToString() ?? "<unknown>"}, isProfileOwner={_isProfileOwner}.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag,
                $"PREPARE_HIDDEN_SHORTCUT hide preflight failed. package={packageName}, attempt={attempt}, exception={exception.GetType().FullName}: {exception}");
        }
    }

    private void ActionCreateHiddenShortcut()
    {
        var metadata = HiddenAppShortcutManager.TryReadMetadataFromIntent(Intent);
        if (metadata is null)
        {
            FinishWithError("Не удалось прочитать данные ярлыка скрытого приложения.");
            return;
        }

        Log.Debug(LogTag, $"Creating or updating pinned shortcut {metadata.ShortcutId} for {metadata.TargetPackage}.");
        var shortcutPreparation = HiddenAppShortcutManager.CreateOrUpdatePinnedShortcut(this, metadata);
        if (!shortcutPreparation.Succeeded)
        {
            FinishWithError(shortcutPreparation.Message);
            return;
        }

        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultMessage, shortcutPreparation.Message);
        result.PutExtra(AndroidCommandContract.ResultHideImmediately, shortcutPreparation.HideImmediately);
        FinishWithResult(Result.Ok, result);
    }

    private void ActionUnfreezeAndLaunch()
    {
        var intent = Intent;
        var packageName = intent?.GetStringExtra("packageName");
        var displayName = intent?.GetStringExtra("displayName") ?? packageName;
        var launchResult = AndroidAppLaunchResult.CommandReceived(packageName, displayName);
        launchResult.Log(LogTag);
        if (string.IsNullOrWhiteSpace(packageName))
        {
            var failedResult = launchResult.Fail(
                AndroidAppLaunchStage.CommandReceived,
                AndroidAppLaunchIssueKind.InvalidRequest,
                "packageName=missing");
            failedResult.Log(LogTag);
            FinishWithResult(Result.Canceled, failedResult.ToIntent());
            return;
        }

        if (!_isProfileOwner)
        {
            var launchRequest = new Intent(AgnosiaActions.UnfreezeAndLaunch);
            launchRequest.PutExtra("packageName", packageName);
            if (!string.IsNullOrWhiteSpace(displayName)) launchRequest.PutExtra("displayName", displayName);

            if (!AndroidIntentApi.TryTransferToProfileAndStartActivity(
                    this,
                    launchRequest,
                    LogTag,
                    $"Android не смог открыть {packageName} в рабочем профиле.",
                    out var error))
            {
                FinishWithError(error ?? $"Android не смог открыть {packageName} в рабочем профиле.");
                return;
            }

            Log.Debug(LogTag, $"Unfreeze-and-launch command forwarded to work profile. package={packageName}.");
            var forwardedResult = launchResult.WithIssue(
                AndroidAppLaunchIssueKind.None,
                "forwarded_to_work_profile",
                message: $"Команда запуска {displayName} передана в рабочий профиль.");
            FinishWithResult(Result.Ok, forwardedResult.ToIntent());
            return;
        }

        try
        {
            var proxyIntent = HiddenAppShortcutManager.CreateInternalLaunchIntent(packageName, label: displayName);
            if (AndroidIntentExtras.ReadParentFrozenCallback(Intent) is { } parentFrozenCallback)
                proxyIntent.PutExtra(AndroidCommandContract.ExtraParentFrozenCallback, parentFrozenCallback);

            launchResult.WriteToIntent(proxyIntent);
            AuthenticationUtility.SignIntent(proxyIntent);
            _pendingProxyLaunchResult = launchResult;
            Log.Debug(LogTag, $"Starting ProxyActivity for launch result. package={packageName}.");
            StartActivityForResult(proxyIntent, ProxyLaunchRequestCode);
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to prepare proxy launch for {packageName}: {exception}");
            var failedResult = launchResult.Fail(
                AndroidAppLaunchStage.CommandReceived,
                AndroidAppLaunchResult.ClassifyStartActivityException(exception),
                exception.ToString());
            failedResult.Log(LogTag);
            FinishWithResult(Result.Canceled, failedResult.ToIntent());
        }
    }

    private void ActionSetCrossProfileInteraction()
    {
        if (!_isProfileOwner || _policyManager is null)
        {
            FinishWithToggleResult(false);
            return;
        }

        var packages = Intent?.GetStringArrayExtra("packages") ?? [];
        FinishWithToggleResult(AndroidPolicyApi.TrySetCrossProfilePackages(
            _policyManager,
            AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType),
            packages,
            LogTag));
    }

    private void ActionSynchronizePreference()
    {
        var intent = Intent;
        var name = intent?.GetStringExtra("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            Finish();
            return;
        }

        if (intent?.HasExtra("boolean") == true)
        {
            var booleanValue = intent.GetBooleanExtra("boolean", false);
            LocalStorageManager.Instance.SetBoolean(name, booleanValue);
            if (string.Equals(name, StorageKeys.LoggingEnabled, StringComparison.Ordinal) && !booleanValue)
                AndroidAppLogArchive.Clear(this);
        }
        else if (intent?.HasExtra("int") == true)
        {
            LocalStorageManager.Instance.SetInt(name, intent.GetIntExtra("int", int.MinValue));
        }

        if (_isProfileOwner)
            AndroidStartup.EnforceWorkProfilePolicies(this);

        FinishWithResult(Result.Ok);
    }

    private async Task ActionWorkAppFrozenAsync(CancellationToken cancellationToken)
    {
        if (_isProfileOwner)
        {
            Finish();
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trigger = Intent?.GetStringExtra(AndroidProfileCommandGateway.ExtraTrigger) ?? "work_app_frozen";

            var result = await WorkAppFrozenHandler.RestoreParentVpnAndHideOverlayAsync(
                this,
                trigger,
                LogTag,
                cancellationToken);
            if (result.Succeeded)
            {
                FinishWithSuccessMessage(result.Message);
                return;
            }

            Log.Warn(LogTag, $"Work-app frozen event handling failed. trigger={trigger}, message={result.Message}");
            FinishWithError(result.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to handle work-app frozen event: {exception}");
            FinishWithError("Android не смог обработать событие заморозки приложения.");
        }
    }

    private void ActionFinalizeProvision()
    {
        if (_isProfileOwner)
        {
            Finish();
            return;
        }

        AndroidPlatformBridge.Instance.NotifyManagedProfileProvisioned(this, Intent);

        var launchIntent = string.IsNullOrWhiteSpace(PackageName)
            ? null
            : PackageManager?.GetLaunchIntentForPackage(PackageName);
        if (launchIntent is not null)
        {
            launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            AndroidIntentApi.TryStartActivity(
                this,
                launchIntent,
                LogTag,
                "Android не смог открыть Agnosia после завершения настройки.",
                out _);
        }

        Toast.MakeText(this, "Настройка Agnosia завершена.", ToastLength.Long)?.Show();
        Finish();
    }

    private void FinishWithSystemAppError()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultError, AndroidCommandContract.ErrorSystemAppUnsupported);
        FinishWithResult(Result.Canceled, result);
    }

    private void FinishWithError(string message)
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultError, message);
        FinishWithResult(Result.Canceled, result);
    }

    private void FinishWithSuccessMessage(string message)
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultMessage, message);
        FinishWithResult(Result.Ok, result);
    }

    private void FinishWithToggleResult(bool success)
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultToggleSuccess, success);
        FinishWithResult(success ? Result.Ok : Result.Canceled, result);
    }

    private void FinishWithResult(Result resultCode, Intent? data = null)
    {
        if (_finishRequested || _destroyCancellation.IsCancellationRequested) return;

        _finishRequested = true;
        Log.Debug(
            LogTag,
            $"Finishing action={Intent?.Action ?? "<none>"} with result={resultCode}, hasData={data is not null}.");
        if (data is null)
            SetResult(resultCode);
        else
            SetResult(resultCode, data);

        Finish();
    }

    internal void HandlePackageInstallerCallback(Intent? intent)
    {
        RunAction(
            cancellationToken => HandlePackageInstallerCallbackAsync(intent, cancellationToken),
            "Android не смог обработать результат установки пакета.");
    }

    private async Task HandlePackageInstallerCallbackAsync(
        Intent? intent,
        CancellationToken cancellationToken)
    {
        var status = (PackageInstallStatus)(intent?.Extras?.GetInt(PackageInstaller.ExtraStatus) ??
                                            (int)PackageInstallStatus.Failure);
        var callbackPackage = intent?.GetStringExtra(AndroidCommandContract.ExtraCallbackPackage)
                              ?? intent?.GetStringExtra(PackageInstaller.ExtraPackageName);
        var operation = intent?.GetStringExtra(AndroidCommandContract.ExtraPackageInstallerOperation);
        var statusMessage = intent?.GetStringExtra(PackageInstaller.ExtraStatusMessage);

        Log.Info(LogTag,
            $"PackageInstaller callback status={status}, operation={operation ?? "<unknown>"}, package={callbackPackage ?? "<unknown>"}, statusMessage={statusMessage ?? "<none>"}.");

        if (status == PackageInstallStatus.PendingUserAction)
        {
            var confirmationIntent = (Intent?)intent?.Extras?.Get(Intent.ExtraIntent);
            if (confirmationIntent is not null)
            {
                if (!AndroidIntentApi.TryStartActivity(
                        this,
                        confirmationIntent,
                        LogTag,
                        "Android не смог открыть подтверждение установки пакета.",
                        out var error))
                    FinishWithError(error ?? "Android не смог открыть подтверждение установки пакета.");

                return;
            }

            FinishWithError("Android запросил подтверждение установки, но не предоставил экран подтверждения.");
            return;
        }

        if (status == PackageInstallStatus.Success)
        {
            if (string.Equals(operation, AndroidCommandContract.PackageInstallerOperationInstall,
                    StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(callbackPackage)
                && !await WaitForPackageAvailableAsync(callbackPackage, cancellationToken))
            {
                FinishWithError($"Android установил {callbackPackage}, но пакет еще не доступен в рабочем профиле.");
                return;
            }

            FinishWithResult(Result.Ok);
            return;
        }

        FinishWithError(string.IsNullOrWhiteSpace(statusMessage)
            ? "Android отклонил установку пакета."
            : $"Android отклонил установку пакета: {statusMessage}");
    }

    private void DeliverPendingPackageInstallerCallback()
    {
        if (PackageInstallerCallbackCoordinator.TakePendingCallback() is { } pendingCallback)
            HandlePackageInstallerCallback(pendingCallback);
    }
}
