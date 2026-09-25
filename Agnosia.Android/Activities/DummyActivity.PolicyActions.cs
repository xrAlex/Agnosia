using Agnosia.Android.Services;
using Agnosia.Android.Permissions;
using Android.App.Admin;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private void ActionSetLockdownEnabled()
    {
        if (!_isProfileOwner || _policyManager is null)
        {
            Log.Warn(LogTag,
                $"Lockdown toggle rejected. isProfileOwner={_isProfileOwner}, hasPolicyManager={_policyManager is not null}.");
            FinishWithError("Lockdown доступен только в рабочем профиле Agnosia.");
            return;
        }

        var enabled = Intent?.GetBooleanExtra(AndroidCommandContract.ExtraPreferenceBoolean, false) == true;
        var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
        var result = LockdownVpnController.SetEnabled(this, _policyManager, admin, enabled);
        if (result.Succeeded)
            FinishWithSuccessMessage(result.Message);
        else
            FinishWithError(result.Message);
    }

    private void ActionSetLockdownInternetAccess()
    {
        var packageName = Intent?.GetStringExtra(AndroidCommandContract.ExtraPackage);
        var blocked = Intent?.GetBooleanExtra(AndroidCommandContract.ExtraInternetBlocked, false) == true;
        if (!_isProfileOwner || _policyManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            Log.Warn(LogTag,
                $"Lockdown package toggle rejected. package={packageName ?? "<none>"}, blocked={blocked}, isProfileOwner={_isProfileOwner}, hasPolicyManager={_policyManager is not null}.");
            FinishWithError("Android не смог изменить Lockdown для приложения рабочего профиля.");
            return;
        }

        if (!LockdownSettingsStore.IsEnabled())
        {
            FinishWithError("Сначала включите модуль Lockdown.");
            return;
        }

        if (AndroidWorkProfilePackageClassifier.IsSystemPackage(PackageManager, packageName))
        {
            FinishWithError("Lockdown не применяется к системным приложениям рабочего профиля.");
            return;
        }

        var previousPackages = LockdownSettingsStore.LoadBlockedPackages();
        LockdownSettingsStore.SetPackageBlocked(packageName, blocked);
        ClearAppInventoryQueryCache();
        var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
        var refreshResult = LockdownVpnController.RefreshPolicy(this, _policyManager, admin);
        if (!refreshResult.Succeeded)
        {
            LockdownSettingsStore.SaveBlockedPackages(previousPackages);
            ClearAppInventoryQueryCache();
            FinishWithError(refreshResult.Message);
            return;
        }

        FinishWithSuccessMessage(blocked
            ? "Интернет приложения заблокирован."
            : "Интернет приложения разблокирован.");
    }

    private async Task ActionFreezePackageAsync(bool hidden, CancellationToken cancellationToken)
    {
        var envelope = new AndroidCommandEnvelope(_commandCorrelationId,
            hidden ? AndroidCommandKind.FreezePackage : AndroidCommandKind.UnfreezePackage,
            AndroidCommandTargetProfile.Work, AndroidCommandInteractivity.NonInteractive,
            AndroidCommandPriority.Mutation, TimeSpan.FromSeconds(30),
            System.Text.Json.JsonSerializer.Serialize(new Commands.Handlers.SetPackageHiddenRequest(
                Intent?.GetStringExtra(AndroidCommandContract.ExtraPackage) ?? "")));
        var context = ServiceRegistry.GetRequiredService<AndroidCommandExecutionContextFactory>()
            .Create(this, this, envelope, AndroidCommandTransportKind.Activity, "dummy-activity");
        var result = await ServiceRegistry.GetRequiredService<AndroidCommandHandlerExecutor>()
            .ExecuteAsync(envelope, context, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) FinishWithSuccessMessage(result.Message);
        else FinishWithError(result.Message, result.ErrorCode);
    }

    private async Task ActionRevokeRuntimePermissionsAsync(CancellationToken cancellationToken)
    {
        var packageName = Intent?.GetStringExtra(AndroidCommandContract.ExtraPackage);
        var permissions = Intent?.GetStringArrayExtra(AndroidCommandContract.ExtraPermissions) ?? [];
        var clearPolicy = Intent?.GetBooleanExtra(AndroidCommandContract.ExtraClearPermissionPolicy, false) == true;
        var revokeWithoutPolicy = Intent?.GetBooleanExtra(AndroidCommandContract.ExtraRevokeWithoutPolicy, false) == true;
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

        if (revokeWithoutPolicy && (clearPolicy || permissions.Length != 1 || PackageManager is null))
        {
            FinishWithError("Неверный запрос на отзыв разрешения.");
            return;
        }

        var failedPermissions = new List<string>();
        var admin = AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType);
        using var operationLease = await HiddenAppSessionConcurrency
            .EnterOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentPermissions = AndroidAppPermissionReader.Read(this, packageName, _policyManager, admin);
        foreach (var permission in permissions)
        {
            var current = currentPermissions.FirstOrDefault(item => item.Name == permission);
            if (current is not { CanChangePolicy: true }
                || (revokeWithoutPolicy && !current.CanRevokeGrant))
            {
                FinishWithError(current?.RestrictionReason ??
                    "Это разрешение сейчас нельзя отозвать. Обновите список разрешений.");
                return;
            }
        }
        if (!TryMakePackageVisibleForPolicyOperation(
                admin,
                packageName,
                "runtime permission revoke",
                out var restoreHiddenState,
                out var visibilitySession,
                out var visibilityError))
        {
            FinishWithError(visibilityError ?? $"Android не смог восстановить {packageName} для отзыва разрешений.");
            return;
        }

        var attemptedPermissions = 0;
        var hiddenStateRestored = true;
        string? restoreError = null;
        try
        {
            foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(permission)) continue;

                attemptedPermissions++;
                var denyResult = revokeWithoutPolicy
                    ? await AndroidPolicyApi.TryRevokeRuntimePermissionOnceAsync(
                        _policyManager, PackageManager!, admin, packageName, permission, LogTag, cancellationToken)
                        .ConfigureAwait(false)
                    : await AndroidPolicyApi.TrySetRuntimePermissionPolicyAsync(
                        _policyManager, admin, packageName, permission,
                        clearPolicy ? PermissionGrantState.Default : PermissionGrantState.Denied,
                        LogTag, cancellationToken).ConfigureAwait(false);
                if (!denyResult.Succeeded)
                    failedPermissions.Add(denyResult.Error ?? permission);
            }
        }
        finally
        {
            ClearAppInventoryQueryCache();
            if (restoreHiddenState)
            {
                hiddenStateRestored = RestoreHiddenStateAfterPolicyOperation(
                    admin,
                    packageName,
                    "runtime permission revoke",
                    visibilitySession,
                    out restoreError);
            }
        }

        if (!hiddenStateRestored)
        {
            FinishWithError(
                restoreError
                ?? $"Разрешения обработаны, но Android не смог снова скрыть {packageName}. Повтор запланирован.");
            return;
        }

        if (failedPermissions.Count == 0)
        {
            FinishWithSuccessMessage(revokeWithoutPolicy
                ? "Доступ отозван. Приложение сможет снова запросить его."
                : clearPolicy
                    ? "Запрет снят. Приложение сможет снова запросить доступ."
                    : $"Разрешения запрещены: {attemptedPermissions}.");
            return;
        }

        FinishWithError(
            string.Join(Environment.NewLine, failedPermissions));
    }
}
