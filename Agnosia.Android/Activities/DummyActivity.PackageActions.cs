using Agnosia.Android.Receivers;
using Android.Content;
using Android.Content.PM;
using AndroidProcess = Android.OS.Process;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private CancellationTokenSource? _packageInstallRecoveryCancellation;
    private InstallSessionCallback? _installSessionCallback;
    private PackageInstaller? _observedInstaller;
    private int? _installSessionId;

    private void HandlePackageInstallerUserActionResult(Result resultCode)
    {
        // Install confirmation is not completion: the session can still be writing.
        Log.Info(LogTag,
            $"Package confirmation closed. correlationId={_commandCorrelationId}, kind={_commandKind}, result={resultCode}; awaiting installer status or verified new package.");
        if (_commandKind == AndroidCommandKind.UninstallPackage)
            CompletePackageRemovalAfterConfirmation();
    }

    private void CompletePackageRemovalAfterConfirmation()
    {
        if (_finishRequested || _destroyCancellation.IsCancellationRequested) return;
        var packageName = Intent?.GetStringExtra(AndroidCommandContract.ExtraPackage);
        if (string.IsNullOrWhiteSpace(packageName) || PackageManager is null) return;

        // Android's uninstaller finishes after deletion, but with a callback it
        // leaves Activity.result at Canceled even on success. On actual dialog
        // cancellation some versions send no installer broadcast at all.
        // Check the package, including uninstalled entries; never infer success
        // or failure from the confirmation Activity's result code alone.
        bool installed;
        try
        {
            var application = PackageManager.GetApplicationInfo(
                packageName, AndroidSystemApi.GetInstalledApplicationFlags());
            installed = application is not null && (application.Flags & ApplicationInfoFlags.Installed) != 0;
        }
        catch (PackageManager.NameNotFoundException)
        {
            installed = false;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Cannot confirm removal of {packageName}: {exception.GetType().Name}.");
            return;
        }

        if (!installed)
        {
            FinishWithResult(Result.Ok);
            return;
        }

        RestoreHiddenStateAfterFailedPackageRemoval(Intent, "package remains after uninstall confirmation closed");
        FinishWithError("Удаление отменено или отклонено Android. Приложение осталось в рабочем профиле.");
    }

    private PendingIntent RegisterPackageInstallerCallback(
        string? packageName, string operation, bool restoreHiddenState = false)
    {
        _packageInstallerOperationId = Guid.NewGuid().ToString("D");
        _packageInstallerCallback = AndroidPendingIntentApi.CreatePackageInstallerCallbackPendingIntent(
            this, typeof(PackageInstallerCallbackReceiver), AgnosiaActions.PackageInstallerCallback,
            packageName, operation, restoreHiddenState, _packageInstallerOperationId);
        // Keep ownership while paused behind Android's confirmation UI. Dashboard
        // query Activities must never receive the result of this operation.
        PackageInstallerCallbackCoordinator.Register(_packageInstallerOperationId, this);
        return _packageInstallerCallback;
    }

    private void ReleasePackageInstallerCallback()
    {
        _packageInstallRecoveryCancellation?.Cancel();
        if (_installSessionCallback is { } observer)
        {
            _installSessionCallback = null;
            try { _observedInstaller?.UnregisterSessionCallback(observer); }
            catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
            {
                Log.Warn(LogTag, $"Cannot unregister install session observer: {exception.GetType().Name}.");
            }
            _observedInstaller = null;
        }
        PackageInstallerCallbackCoordinator.Unregister(_packageInstallerOperationId, this);
        _packageInstallerOperationId = null;
        _packageInstallerCallback?.Cancel();
        _packageInstallerCallback?.Dispose();
        _packageInstallerCallback = null;
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

        // Capture state before starting the session: an old installed copy is
        // not evidence that a later update succeeded.
        bool? initiallyInstalled = null;
        try
        {
            initiallyInstalled = IsPackageInstalledForRecovery(packageName);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag,
                $"Cannot read initial package state; awaiting installer callback. package={packageName}, error={exception.GetType().Name}.");
        }
        var wasInstalled = initiallyInstalled != false;
        var reuseExistingWorkCopy = intent.GetBooleanExtra(AndroidCommandContract.ExtraReuseExistingWorkCopy, false);
        long? sourceVersionCode = reuseExistingWorkCopy
                                   && intent.HasExtra(AndroidCommandContract.ExtraSourceVersionCode)
            ? intent.GetLongExtra(AndroidCommandContract.ExtraSourceVersionCode, -1)
            : null;
        long? workVersionCode = null;
        if (reuseExistingWorkCopy && initiallyInstalled == true && !string.IsNullOrWhiteSpace(packageName))
        {
            try
            {
                workVersionCode = PackageManager?.GetPackageInfo(
                    packageName, AndroidSystemApi.GetInstalledApplicationFlags())?.LongVersionCode;
            }
            catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                              || AndroidRecoverableException.IsMatch(exception))
            {
                Log.Warn(LogTag,
                    $"Cannot read work package version before copying. package={packageName}, error={exception.GetType().Name}.");
            }
        }
        Log.Info(LogTag,
            $"Install requested. correlationId={_commandCorrelationId}, package={packageName}, wasInstalled={wasInstalled}, sourceVersionCode={sourceVersionCode?.ToString() ?? "unknown"}, workVersionCode={workVersionCode?.ToString() ?? "unknown"}, user={AndroidProcess.MyUserHandle()}.");
        LogInstallPackageState(packageName, "before_install");
        if (PackageCopyPreflight.CanReuseExistingPackage(
                reuseExistingWorkCopy, _isProfileOwner, initiallyInstalled,
                sourceVersionCode, workVersionCode))
        {
            Log.Info(LogTag, $"Reusing installed work copy. correlationId={_commandCorrelationId}, package={packageName}, user={AndroidProcess.MyUserHandle()}.");
            FinishWithSuccessMessage("Приложение уже установлено в рабочем профиле.");
            return;
        }
        if (string.IsNullOrWhiteSpace(intent.GetStringExtra("apk")))
        {
            FinishWithError(
                "Android не смог установить приложение в рабочий профиль: APK недоступен для копирования.");
            return;
        }
        var callbackPendingIntent = RegisterPackageInstallerCallback(
            packageName, AndroidCommandContract.PackageInstallerOperationInstall);
        if (!AndroidPackageApi.TryStartInstall(
                this,
                packageName,
                intent.GetStringExtra("apk"),
                intent.GetStringArrayExtra("split_apks"),
                callbackPendingIntent,
                LogTag,
                FinishWithError,
                sessionId => ObserveInstallSession(sessionId, packageName)))
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        if (!_finishRequested && !string.IsNullOrWhiteSpace(packageName))
            RunAction(token => RecoverNewPackageInstallationAsync(packageName, wasInstalled, token),
                "Android не смог проверить завершение установки.");
    }

    private bool IsPackageInstalledForRecovery(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName) || PackageManager is null)
            throw new InvalidOperationException("Package state is unavailable.");
        try
        {
            var application = PackageManager.GetApplicationInfo(
                packageName, AndroidSystemApi.GetInstalledApplicationFlags());
            return application is not null && (application.Flags & ApplicationInfoFlags.Installed) != 0;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    private async Task RecoverNewPackageInstallationAsync(
        string packageName, bool wasInstalled, CancellationToken cancellationToken)
    {
        var recovery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _packageInstallRecoveryCancellation = recovery;
        // Leave time for a signed failure to reach the caller before its 3-minute deadline.
        recovery.CancelAfter(TimeSpan.FromSeconds(165));
        try
        {
            var installed = false;
            try
            {
                installed = await Task.Run(() => NewPackageInstallRecovery.WaitAsync(wasInstalled,
                    () => IsPackageInstalledForRecovery(packageName),
                    token => Task.Delay(PackageAvailabilityRetryDelay, token), recovery.Token), recovery.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
            {
                // The original installer callback can still complete the operation.
                Log.Warn(LogTag, $"Package recovery unavailable. package={packageName}, error={exception.GetType().Name}.");
            }
            if (!installed)
            {
                // Keep the command deadline even when an old copy prevents recovery.
                await Task.Delay(Timeout.InfiniteTimeSpan, recovery.Token).ConfigureAwait(false);
                return;
            }

            RunOnUiThread(() =>
            {
                if (_finishRequested || cancellationToken.IsCancellationRequested) return;
                Log.Info(LogTag,
                    $"Recovered install result from new package. correlationId={_commandCorrelationId}, operationId={_packageInstallerOperationId}, package={packageName}.");
                FinishWithResult(Result.Ok);
            });
        }
        catch (OperationCanceledException) when (recovery.IsCancellationRequested)
        {
            // Normal completion or destruction also cancels this observer.
            if (cancellationToken.IsCancellationRequested || _finishRequested) return;
            Log.Warn(LogTag,
                $"Install was not confirmed before timeout. correlationId={_commandCorrelationId}, sessionId={_installSessionId}, package={packageName}, wasInstalled={wasInstalled}.");
            LogInstallPackageState(packageName, "timeout");
            FinishWithError("Android не подтвердил завершение установки вовремя. Обновите список приложений, чтобы проверить результат.");
        }
        finally
        {
            // ReleasePackageInstallerCallback runs on the UI thread as well.
            RunOnUiThread(() =>
            {
                if (ReferenceEquals(_packageInstallRecoveryCancellation, recovery))
                    _packageInstallRecoveryCancellation = null;
                recovery.Dispose();
            });
        }
    }

    private void ObserveInstallSession(int sessionId, string? packageName)
    {
        _installSessionId = sessionId;
        var installer = PackageManager?.PackageInstaller;
        if (installer is null) return;
        var completion = new PackageInstallSessionCompletion(sessionId);
        var observer = new InstallSessionCallback(completion);
        try
        {
            installer.RegisterSessionCallback(observer, new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!));
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            observer.Dispose();
            Log.Warn(LogTag, $"Cannot observe install session. sessionId={sessionId}, error={exception.GetType().Name}.");
            return;
        }
        _observedInstaller = installer;
        _installSessionCallback = observer;
        RunAction(async token =>
        {
            var success = await completion.Completion.WaitAsync(token);
            if (_finishRequested || token.IsCancellationRequested) return;
            Log.Info(LogTag, $"Install session finished. correlationId={_commandCorrelationId}, sessionId={sessionId}, package={packageName}, success={success}, user={AndroidProcess.MyUserHandle()}.");
            LogInstallPackageState(packageName, "session_finished");
            if (!success)
            {
                FinishWithError("Android сообщил об ошибке или отмене установки в рабочий профиль.");
                return;
            }
            await CompleteSuccessfulInstallAsync(packageName, token);
        }, "Android не смог проверить результат сессии установки.");
    }

    private void LogInstallPackageState(string? packageName, string stage)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return;
        try
        {
            var installed = IsPackageInstalledForRecovery(packageName);
            var hidden = _isProfileOwner && _policyManager?.IsApplicationHidden(
                AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType), packageName) == true;
            Log.Info(LogTag, $"Install package state. stage={stage}, correlationId={_commandCorrelationId}, sessionId={_installSessionId}, package={packageName}, installed={installed}, hidden={hidden}, user={AndroidProcess.MyUserHandle()}.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Install package state unavailable. stage={stage}, package={packageName}, error={exception.GetType().Name}.");
        }
    }

    private sealed class InstallSessionCallback(PackageInstallSessionCompletion completion) : PackageInstaller.SessionCallback
    {
        public override void OnFinished(int sessionId, bool success) => completion.ReportFinished(sessionId, success);
        public override void OnCreated(int sessionId) { }
        public override void OnBadgingChanged(int sessionId) { }
        public override void OnActiveChanged(int sessionId, bool active) { }
        public override void OnProgressChanged(int sessionId, float progress) { }
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
            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
            if (!TryClearHiddenStateBeforePackageRemoval(
                    admin,
                    packageName,
                    out _,
                    out var unhideError))
            {
                FinishWithError(unhideError ?? $"Android не смог восстановить {packageName} перед удалением.");
                return;
            }

            if (!AndroidPolicyApi.TrySetApplicationHidden(
                    _policyManager,
                    admin,
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

        if (_isProfileOwner && _policyManager is not null)
        {
            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
            if (!TryClearHiddenStateBeforePackageRemoval(
                    admin,
                    packageName,
                    out var restoreHiddenState,
                    out var unhideError))
            {
                FinishWithError(unhideError ?? $"Android не смог восстановить {packageName} перед удалением.");
                return;
            }

            intent.PutExtra(AndroidCommandContract.ExtraRestoreHiddenState, restoreHiddenState);
        }

        intent.PutExtra(AndroidCommandContract.ExtraCallbackPackage, packageName);
        intent.PutExtra(
            AndroidCommandContract.ExtraPackageInstallerOperation,
            AndroidCommandContract.PackageInstallerOperationUninstall);

        var shouldRestoreHiddenState = intent.GetBooleanExtra(
            AndroidCommandContract.ExtraRestoreHiddenState,
            false);

        var pendingIntent = RegisterPackageInstallerCallback(
            packageName, AndroidCommandContract.PackageInstallerOperationUninstall, shouldRestoreHiddenState);
        if (AndroidPackageApi.TryStartUninstall(this, packageName, pendingIntent)) return;

        RestoreHiddenStateAfterFailedPackageRemoval(intent, "package removal did not start");
        FinishWithError($"Android не смог начать удаление {packageName}.");
    }

    private bool TryClearHiddenStateBeforePackageRemoval(
        ComponentName admin,
        string packageName,
        out bool restoreHiddenState,
        out string? error)
    {
        restoreHiddenState = false;
        error = null;
        if (_policyManager is null)
        {
            error = $"Android не смог проверить состояние {packageName} перед удалением.";
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
                $"Could not read hidden state before package removal. package={packageName}, error={exception.GetType().Name}.");
            error = $"Android не смог проверить состояние {packageName} перед удалением.";
            return false;
        }

        if (!isHidden) return true;

        if (AndroidPolicyApi.TrySetApplicationHidden(
                _policyManager,
                admin,
                packageName,
                false,
                LogTag,
                out error))
        {
            restoreHiddenState = true;
            return true;
        }

        error ??= $"Android не смог восстановить {packageName} перед удалением.";
        return false;
    }

    private void RestoreHiddenStateAfterFailedPackageRemoval(Intent? source, string operation)
    {
        if (!string.Equals(
                source?.GetStringExtra(AndroidCommandContract.ExtraPackageInstallerOperation),
                AndroidCommandContract.PackageInstallerOperationUninstall,
                StringComparison.Ordinal)
            || !PackageRemovalVisibility.ShouldRollback(
                source?.GetBooleanExtra(AndroidCommandContract.ExtraRestoreHiddenState, false) == true,
                uninstallSucceeded: false)
            || !_isProfileOwner
            || _policyManager is null)
            return;

        var packageName = source?.GetStringExtra(AndroidCommandContract.ExtraCallbackPackage)
                          ?? source?.GetStringExtra(AndroidCommandContract.ExtraPackage);
        if (string.IsNullOrWhiteSpace(packageName))
        {
            Log.Warn(LogTag, $"Cannot restore hidden state after {operation}: package name is missing.");
            return;
        }

        var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
        RestoreHiddenStateAfterPolicyOperation(admin, packageName, operation, null, out _);
    }
}
