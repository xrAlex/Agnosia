using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Receivers;
using Android.App;
using Android.App.Admin;
using Android.Content;
using Exception = System.Exception;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private void HandlePackageInstallerUserActionResult(Result resultCode)
    {
        DeliverPendingPackageInstallerCallback();
        if (_finishRequested) return;

        if (resultCode == Result.Canceled)
            FinishWithError("Операция с пакетом отменена пользователем.");
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
            var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
            if (!TryClearHiddenStateBeforePackageRemoval(admin, packageName, out var unhideError))
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
            if (!TryClearHiddenStateBeforePackageRemoval(admin, packageName, out var unhideError))
            {
                FinishWithError(unhideError ?? $"Android не смог восстановить {packageName} перед удалением.");
                return;
            }
        }

        var pendingIntent = AndroidPendingIntentApi.CreatePackageInstallerCallbackPendingIntent(
            this,
            typeof(PackageInstallerCallbackReceiver),
            AgnosiaActions.PackageInstallerCallback,
            packageName,
            AndroidCommandContract.PackageInstallerOperationUninstall);
        if (!AndroidPackageApi.TryStartUninstall(this, packageName, pendingIntent)) FinishWithResult(Result.Canceled);
    }

    private bool TryClearHiddenStateBeforePackageRemoval(
        ComponentName admin,
        string packageName,
        out string? error)
    {
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
            return true;
        }

        if (!isHidden) return true;

        if (AndroidPolicyApi.TrySetApplicationHidden(
                _policyManager,
                admin,
                packageName,
                false,
                LogTag,
                out error))
            return true;

        error ??= $"Android не смог восстановить {packageName} перед удалением.";
        return false;
    }
}
