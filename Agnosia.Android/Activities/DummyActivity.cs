using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Files;
using Agnosia.Android.Infrastructure;
using Agnosia.Android.Platform;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Android.Shortcuts;
using Agnosia.Android.Vpn;
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
    Permission = "com.agnosia.app.permission.CROSS_PROFILE_COMMAND",
    ExcludeFromRecents = true,
    LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
[
    AgnosiaActions.FinalizeProvision,
    AgnosiaActions.RecoverAuthentication,
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
    AgnosiaActions.QueryAllFilesAccess,
    AgnosiaActions.RequestAllFilesAccess,
    AgnosiaActions.InstallPackage,
    AgnosiaActions.UninstallPackage,
    AgnosiaActions.FreezePackage,
    AgnosiaActions.UnfreezePackage,
    AgnosiaActions.RevokeRuntimePermissions,
    AgnosiaActions.PrepareHiddenShortcut,
    AgnosiaActions.CreateHiddenShortcut,
    AgnosiaActions.UnfreezeAndLaunch,
    AgnosiaActions.SetCrossProfileInteraction,
    AgnosiaActions.StartFileShuttleParentToWork,
    AgnosiaActions.StartFileShuttleWorkToParent,
    AgnosiaActions.SynchronizePreference,
    AgnosiaActions.WorkAppFrozen,
    AgnosiaActions.PackageInstallerCallback
], Categories = [Intent.CategoryDefault])]
public sealed partial class DummyActivity : Activity
{
    private const string LogTag = "AgnosiaDummyActivity";
    private const int ProxyLaunchRequestCode = 7201;
    private const int PackageInstallerUserActionRequestCode = 7202;
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
        CloseFileShuttleConnections();
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
        if (requestCode == PackageInstallerUserActionRequestCode)
        {
            HandlePackageInstallerUserActionResult(resultCode);
            return;
        }

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

            var isSystem = Intent?.GetBooleanExtra(AndroidCommandContract.ExtraIsSystem, false) == true
                           || AndroidWorkProfilePackageClassifier.IsSystemPackage(PackageManager, packageName);
            Log.Debug(LogTag, $"Starting hidden shortcut preparation for {packageName}.");
            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);

            if (!TryMakePackageVisibleForPolicyOperation(
                    admin,
                    packageName,
                    "hidden shortcut preparation",
                    out var restoreHiddenState,
                    out var visibilityError))
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
                            isSystem,
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

                var hideError = isSystem
                    ? null
                    : await Task.Run(
                        () => TryHidePackageAfterInstallAsync(admin, packageName, cancellationToken),
                        cancellationToken);
                var preHideSucceeded = hideError is null;
                if (preHideSucceeded)
                {
                    restoreHiddenState = false;
                    Log.Info(LogTag, isSystem
                        ? $"System app {packageName} shortcut prepared without freezing."
                        : $"Installed hidden app {packageName} was frozen before shortcut creation.");
                }
                else
                {
                    Log.Warn(LogTag,
                        $"Stopping hidden shortcut creation after pre-hide failure. package={packageName}, error={hideError}");
                    FinishWithError(hideError ?? $"Android не смог скрыть {packageName}.");
                    return;
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
                    RestoreHiddenStateAfterPolicyOperation(admin, packageName, "incomplete hidden shortcut preparation");
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

    private bool TryMakePackageVisibleForPolicyOperation(
        ComponentName admin,
        string packageName,
        string operation,
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
                $"Could not read hidden state before {operation}. package={packageName}, exception={exception.GetType().FullName}: {exception.Message}");
            return true;
        }

        if (!isHidden) return true;

        Log.Info(LogTag,
            $"Package {packageName} is hidden before {operation}; temporarily unhiding it.");
        if (AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, false, LogTag, out error))
        {
            restoreHiddenState = true;
            return true;
        }

        error ??= $"Android не смог восстановить {packageName} для операции {operation}.";
        return false;
    }

    private void RestoreHiddenStateAfterPolicyOperation(ComponentName admin, string packageName, string operation)
    {
        if (_policyManager is null) return;

        Log.Info(LogTag,
            $"Restoring hidden state after {operation}. package={packageName}.");
        if (!AndroidPolicyApi.TrySetApplicationHidden(_policyManager, admin, packageName, true, LogTag, out var error))
            Log.Warn(LogTag,
                $"Failed to restore hidden state after {operation}. package={packageName}, error={error ?? "<none>"}.");
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

        var isSystem = intent?.GetBooleanExtra(AndroidCommandContract.ExtraIsSystem, false) == true;
        if (!_isProfileOwner)
        {
            var launchRequest = new Intent(AgnosiaActions.UnfreezeAndLaunch);
            launchRequest.PutExtra("packageName", packageName);
            if (!string.IsNullOrWhiteSpace(displayName)) launchRequest.PutExtra("displayName", displayName);
            launchRequest.PutExtra(AndroidCommandContract.ExtraIsSystem, isSystem);

            if (!AgnosiaUtilities.TryTransferToProfileAndStartActivity(
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
            if (isSystem || AndroidWorkProfilePackageClassifier.IsSystemPackage(PackageManager, packageName))
            {
                LaunchSystemWorkProfilePackage(packageName, displayName, launchResult);
                return;
            }

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

    private void LaunchSystemWorkProfilePackage(
        string packageName,
        string? displayName,
        AndroidAppLaunchResult launchResult)
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(packageName);
        if (launchIntent is null)
        {
            var failedResult = launchResult.Fail(
                AndroidAppLaunchStage.CommandReceived,
                AndroidAppLaunchIssueKind.MissingLauncherActivity,
                "system_work_app_launchIntent=null");
            failedResult.Log(LogTag);
            FinishWithResult(Result.Canceled, failedResult.ToIntent());
            return;
        }

        launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
        try
        {
            StartActivity(launchIntent);
            var successResult = launchResult
                .WithDisplayName(displayName)
                .WithStage(AndroidAppLaunchStage.StartActivityAttempted, "system_work_app_direct_launch");
            successResult.Log(LogTag);
            FinishWithResult(Result.Ok, successResult.ToIntent());
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to launch system work app {packageName}: {exception}");
            var failedResult = launchResult.Fail(
                AndroidAppLaunchStage.StartActivityFailedWithException,
                AndroidAppLaunchResult.ClassifyStartActivityException(exception),
                exception.ToString());
            failedResult.Log(LogTag);
            FinishWithResult(Result.Canceled, failedResult.ToIntent());
        }
    }

}
