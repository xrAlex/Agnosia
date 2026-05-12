using System.Text.Json;
using Agnosia.Android.Api;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Android.Shortcuts;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Java.Lang;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.AgnosiaLog;

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
    AgnosiaActions.QueryLogs,
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
    private static readonly Lock PackageInstallerCallbackSync = new();
    private static DummyActivity? _activeInstance;
    private static Intent? _pendingPackageInstallerCallback;
    private static readonly Type AdminReceiverType = typeof(AgnosiaDeviceAdminReceiver);
    private static readonly Type MainActivityType = typeof(MainActivity);

    private DevicePolicyManager? _policyManager;
    private bool _isProfileOwner;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AgnosiaRuntime.Initialize(this);

        _policyManager = AndroidSystemApi.GetDevicePolicyManager(this);
        _isProfileOwner = _policyManager?.IsProfileOwnerApp(PackageName) == true;
        if (_isProfileOwner)
        {
            AgnosiaUtilities.EnforceWorkProfilePolicies(
                this,
                AdminReceiverType,
                MainActivityType,
                string.Equals(Intent?.Action, AgnosiaActions.FinalizeProvision, StringComparison.Ordinal));
            AgnosiaUtilities.EnforceUserRestrictions(this, AdminReceiverType);
            WorkProfileLockFreezeService.EnsureRunning(this);
        }

        HandleAction();
    }

    protected override void OnResume()
    {
        base.OnResume();

        lock (PackageInstallerCallbackSync)
        {
            _activeInstance = this;
        }

        DeliverPendingPackageInstallerCallback();
    }

    protected override void OnPause()
    {
        lock (PackageInstallerCallbackSync)
        {
            if (ReferenceEquals(_activeInstance, this))
            {
                _activeInstance = null;
            }
        }

        base.OnPause();
    }

    protected override void OnDestroy()
    {
        lock (PackageInstallerCallbackSync)
        {
            if (ReferenceEquals(_activeInstance, this))
            {
                _activeInstance = null;
            }
        }

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
            Log.Warn(LogTag, $"Rejected signed action={action}: authentication check failed. isProfileOwner={_isProfileOwner}.");
            Finish();
            return;
        }

        try
        {
            switch (action)
            {
                case AgnosiaActions.ProfilePing:
                    if (!_isProfileOwner)
                    {
                        FinishWithError("Рабочий профиль не управляется Agnosia.");
                        break;
                    }

                    if (_isProfileOwner)
                    {
                        AgnosiaUtilities.EnforceWorkProfilePolicies(
                            this,
                            AdminReceiverType,
                            MainActivityType);
                        AgnosiaUtilities.EnforceUserRestrictions(this, AdminReceiverType);
                        WorkProfileLockFreezeService.EnsureRunning(this);
                    }

                    FinishWithResult(Result.Ok);
                    break;
                case AgnosiaActions.QueryApps:
                    ActionQueryApps();
                    break;
                case AgnosiaActions.QueryLogs:
                    ActionQueryLogs();
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
                    ActionPrepareHiddenShortcut();
                    break;
                case AgnosiaActions.CreateHiddenShortcut:
                    ActionCreateHiddenShortcut();
                    break;
                case AgnosiaActions.FreezePackage:
                    ActionFreezePackage(hidden: true);
                    break;
                case AgnosiaActions.UnfreezePackage:
                    ActionFreezePackage(hidden: false);
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
                    ActionWorkAppFrozen();
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

    private void ActionQueryApps()
    {
        var intent = Intent;
        var packageManager = PackageManager;
        if (intent is null || packageManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        var showAll = intent.GetBooleanExtra("show_all", false);
        var admin = _isProfileOwner && _policyManager is not null
            ? AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType)
            : null;
        var models = AndroidAppInventoryApi.QueryInstalledApps(this, packageManager, _policyManager, admin, showAll);

        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultAppsJson, JsonSerializer.Serialize(models));

        if (admin is not null && _policyManager is not null)
        {
            result.PutExtra(AndroidCommandContract.ResultInteractionPackages, AndroidPolicyApi.GetCrossProfilePackages(_policyManager, admin));
        }

        FinishWithResult(Result.Ok, result);
    }

    private void ActionQueryLogs()
    {
        var result = new Intent();
        result.PutExtra(
            AndroidCommandContract.ResultLogsJson,
            JsonSerializer.Serialize(AndroidAppLogArchive.Load(this)));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionQueryUsageStatsAccess()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultUsageStatsAccess, AndroidUsageStatsAccessApi.HasAccess(this, LogTag));
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
        {
            FinishWithSuccessMessage("Откройте Agnosia в настройках Android и включите доступ к истории использования.");
        }
    }

    private void ActionQueryPackageInstallAccess()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultPackageInstallAccess, AndroidPackageApi.CanRequestInstalls(this, LogTag));
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
        {
            FinishWithSuccessMessage("Включите установку APK из Agnosia в рабочем профиле.");
        }
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

        var callbackPendingIntent = AndroidPendingIntentApi.CreatePackageInstallerCallbackPendingIntent(
            this,
            typeof(PackageInstallerCallbackReceiver),
            AgnosiaActions.PackageInstallerCallback);
        if (!AndroidPackageApi.TryStartInstall(
            this,
            packageName,
            intent.GetStringExtra("apk"),
            intent.GetStringArrayExtra("split_apks"),
            callbackPendingIntent,
            LogTag,
            FinishWithError))
        {
            FinishWithResult(Result.Canceled);
        }
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
                hidden: true,
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
            AgnosiaActions.PackageInstallerCallback);
        if (!AndroidPackageApi.TryStartUninstall(this, packageName, pendingIntent))
        {
            FinishWithResult(Result.Canceled);
        }
    }

    private void ActionFreezePackage(bool hidden)
    {
        var packageName = Intent?.GetStringExtra("package");
        Log.Info(LogTag, $"Freeze package command received. package={packageName ?? "<none>"}, hidden={hidden}, isProfileOwner={_isProfileOwner}.");
        if (!_isProfileOwner || _policyManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            Log.Warn(LogTag, $"Freeze package command rejected. package={packageName ?? "<none>"}, hidden={hidden}, isProfileOwner={_isProfileOwner}, hasPolicyManager={_policyManager is not null}.");
            FinishWithResult(Result.Canceled);
            return;
        }

        var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
        if (!AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, hidden, LogTag, out var error))
        {
            FinishWithError(error ?? (hidden
                ? $"Android не смог скрыть {packageName}."
                : $"Android не смог восстановить {packageName}."));
            return;
        }

        Log.Info(LogTag, $"Freeze package command completed. package={packageName}, hidden={hidden}.");
        FinishWithSuccessMessage(hidden
            ? "Приложение скрыто."
            : "Приложение снова доступно в рабочем профиле.");
    }

    private async void ActionPrepareHiddenShortcut()
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

            Log.Info(LogTag, $"Starting hidden shortcut preparation for {packageName}.");

            HiddenAppShortcutBuildResult metadataResult;
            try
            {
                metadataResult = await HiddenAppShortcutManager.BuildMetadataAsync(this, packageName);
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

            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
            string? hideError = null;
            if (_policyManager is null
                || !AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, hidden: true, LogTag, out hideError))
            {
                FinishWithError(hideError ?? $"Android не смог скрыть {packageName} после установки.");
                return;
            }

            Log.Info(LogTag, $"Installed hidden app {packageName} was frozen before shortcut creation.");

            var result = new Intent();
            HiddenAppShortcutManager.WriteMetadataToIntent(result, metadataResult.Metadata);
            FinishWithResult(Result.Ok, result);
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"ActionPrepareHiddenShortcut failed: {ex}");
            FinishWithError("Android не смог подготовить ярлык скрытого приложения.");
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

        Log.Info(LogTag, $"Creating or updating pinned shortcut {metadata.ShortcutId} for {metadata.TargetPackage}.");
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
        if (string.IsNullOrWhiteSpace(packageName))
        {
            Finish();
            return;
        }

        if (!_isProfileOwner)
        {
            var launchRequest = new Intent(AgnosiaActions.UnfreezeAndLaunch);
            launchRequest.PutExtra("packageName", packageName);
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

            FinishWithResult(Result.Ok);
            return;
        }

        try
        {
            var proxyIntent = HiddenAppShortcutManager.CreateInternalLaunchIntent(packageName);
            if (ReadParentFrozenCallback(Intent) is { } parentFrozenCallback)
            {
                proxyIntent.PutExtra(AndroidCommandContract.ExtraParentFrozenCallback, parentFrozenCallback);
            }

            if (AndroidIntentApi.TryStartActivity(
                this,
                proxyIntent,
                LogTag,
                $"Android не смог открыть {packageName}.",
                out var error))
            {
                FinishWithSuccessMessage("Открываем приложение.");
                return;
            }

            FinishWithError(error ?? $"Android не смог открыть {packageName}.");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to prepare proxy launch for {packageName}: {exception}");
            FinishWithError($"Android не смог открыть {packageName}.");
        }
    }

    private static PendingIntent? ReadParentFrozenCallback(Intent? intent)
    {
        if (intent is null)
        {
            return null;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return intent.GetParcelableExtra(
                AndroidCommandContract.ExtraParentFrozenCallback,
                Class.FromType(typeof(PendingIntent))) as PendingIntent;
        }

#pragma warning disable CA1422
        return intent.GetParcelableExtra(AndroidCommandContract.ExtraParentFrozenCallback) as PendingIntent;
#pragma warning restore CA1422
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
            {
                AndroidAppLogArchive.Clear(this);
            }
        }
        else if (intent?.HasExtra("int") == true)
        {
            LocalStorageManager.Instance.SetInt(name, intent.GetIntExtra("int", int.MinValue));
        }

        if (_isProfileOwner)
        {
            AgnosiaUtilities.EnforceWorkProfilePolicies(this, AdminReceiverType, MainActivityType);
        }

        FinishWithResult(Result.Ok);
    }

    private async void ActionWorkAppFrozen()
    {
        if (_isProfileOwner)
        {
            Finish();
            return;
        }

        try
        {
            var trigger = Intent?.GetStringExtra(AndroidProfileCommandGateway.ExtraTrigger) ?? "work_app_frozen";
            Log.Info(LogTag, $"Work-app frozen event received in parent profile. trigger={trigger}");

            // The overlay is already visible from the work-app launch; VPN TempActivity
            // will start without BAL_BLOCK because Android sees a visible non-app window.
            var result = await AndroidVpnAutomationApi.EnableConfiguredVpnAfterWorkFreezeAsync(this, trigger);
            if (result.Succeeded)
            {
                Log.Info(LogTag, $"Work-app frozen event handled successfully. trigger={trigger}, message={result.Message}");
                FinishWithSuccessMessage(result.Message);
                return;
            }

            Log.Warn(LogTag, $"Work-app frozen event handling failed. trigger={trigger}, message={result.Message}");
            FinishWithError(result.Message);
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to handle work-app frozen event: {exception}");
            FinishWithError("Android не смог обработать событие заморозки приложения.");
        }
        finally
        {
            // Hide overlay regardless of VPN start success/failure.
            try
            {
                OverlayVpnService.HideOverlay(this);
            }
            catch (Exception hideException)
            {
                Log.Warn(LogTag, $"Failed to hide overlay after work-app frozen: {hideException.Message}");
            }
        }
    }

    private void ActionFinalizeProvision()
    {
        if (_isProfileOwner)
        {
            Finish();
            return;
        }

        AgnosiaUtilities.MarkWorkProfileReady();

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
        if (data is null)
        {
            SetResult(resultCode);
        }
        else
        {
            SetResult(resultCode, data);
        }

        Finish();
    }

    private void HandlePackageInstallerCallback(Intent? intent)
    {
        var status = (PackageInstallStatus)(intent?.Extras?.GetInt(PackageInstaller.ExtraStatus) ?? (int)PackageInstallStatus.Failure);

        Log.Info(LogTag, $"PackageInstaller callback status={status}.");

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
                {
                    FinishWithError(error ?? "Android не смог открыть подтверждение установки пакета.");
                }

                return;
            }

            FinishWithError("Android запросил подтверждение установки, но не предоставил экран подтверждения.");
            return;
        }

        if (status == PackageInstallStatus.Success)
        {
            FinishWithResult(Result.Ok);
            return;
        }

        FinishWithError("Android отклонил установку пакета.");
    }

    private void DeliverPendingPackageInstallerCallback()
    {
        Intent? pendingIntent = null;
        lock (PackageInstallerCallbackSync)
        {
            if (_pendingPackageInstallerCallback is not null)
            {
                pendingIntent = new Intent(_pendingPackageInstallerCallback);
                _pendingPackageInstallerCallback = null;
            }
        }

        if (pendingIntent is not null)
        {
            HandlePackageInstallerCallback(pendingIntent);
        }
    }

    internal static void DispatchPackageInstallerCallback(Intent intent)
    {
        DummyActivity? activity;
        lock (PackageInstallerCallbackSync)
        {
            activity = _activeInstance;
            if (activity is null)
            {
                _pendingPackageInstallerCallback = new Intent(intent);
                return;
            }
        }

        activity.RunOnUiThread(() => activity.HandlePackageInstallerCallback(new Intent(intent)));
    }
}
