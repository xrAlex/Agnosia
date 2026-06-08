using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Platform;
using Android.App;
using Android.Content.PM;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
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

        if (hidden && AndroidWorkProfilePackageClassifier.IsSystemPackage(PackageManager, packageName))
        {
            Log.Info(LogTag, $"Ignoring freeze command for system work-profile app. package={packageName}.");
            FinishWithSuccessMessage("Системные приложения рабочего профиля не замораживаются Agnosia.");
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

    private void ActionRevokeRuntimePermissions()
    {
        var packageName = Intent?.GetStringExtra(AndroidCommandContract.ExtraPackage);
        var permissions = Intent?.GetStringArrayExtra(AndroidCommandContract.ExtraPermissions) ?? [];
        if (!_isProfileOwner || _policyManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            Log.Warn(LogTag,
                $"Runtime permission revoke rejected. package={packageName ?? "<none>"}, isProfileOwner={_isProfileOwner}, hasPolicyManager={_policyManager is not null}.");
            FinishWithError("Android не смог отозвать runtime-разрешения в рабочем профиле.");
            return;
        }

        if (permissions.Length == 0)
        {
            FinishWithSuccessMessage("У приложения нет runtime-разрешений для отзыва.");
            return;
        }

        var failedPermissions = new List<string>();
        var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
        if (!TryMakePackageVisibleForPolicyOperation(
                admin,
                packageName,
                "runtime permission revoke",
                out var restoreHiddenState,
                out var visibilityError))
        {
            FinishWithError(visibilityError ?? $"Android не смог восстановить {packageName} для отзыва разрешений.");
            return;
        }

        var attemptedPermissions = 0;
        foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(permission)) continue;

            attemptedPermissions++;
            if (!AndroidPolicyApi.TryDenyRuntimePermission(
                    _policyManager,
                    PackageManager,
                    admin,
                    packageName,
                    permission,
                    LogTag,
                    out _))
                failedPermissions.Add(permission);
        }

        if (restoreHiddenState)
            RestoreHiddenStateAfterPolicyOperation(admin, packageName, "runtime permission revoke");

        if (failedPermissions.Count == 0)
        {
            FinishWithSuccessMessage($"Runtime-разрешения отозваны: {attemptedPermissions}.");
            return;
        }

        FinishWithError(
            $"Не удалось отозвать runtime-разрешения: {string.Join(", ", failedPermissions.Select(FormatPermissionName))}.");
    }
}
