using Agnosia.Android.Receivers;
using Android.Content;
using Android.Content.PM;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private void HandlePackageInstallerUserActionResult(Result resultCode)
    {
        // Install confirmation is not completion: the session can still be writing.
        Log.Debug(LogTag, $"Package confirmation closed with result={resultCode}; awaiting installer status.");
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
