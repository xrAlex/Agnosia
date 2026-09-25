using System.Runtime.Versioning;
using Agnosia.Android.Api.Internal;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Platform;

public static class AndroidPolicyApi
{
    private const int RuntimePermissionRevokeConfirmationAttempts = 20;
    private const int RuntimePermissionRevokeConfirmationDelayMilliseconds = 50;

    public static void ApplyCrossProfileContactsPolicy(DevicePolicyManager manager, ComponentName admin, bool disabled)
    {
        if (AndroidApiLevel.IsAtLeastUpsideDownCake())
        {
            SetManagedProfileContactsAccessPolicy(manager, disabled);
            return;
        }

        SetCrossProfileContactsSearchDisabled(manager, admin, disabled);
    }

    public static void DisableParentProfileAppLinking(DevicePolicyManager manager, ComponentName admin)
    {
        manager.ClearUserRestriction(admin, UserManager.AllowParentProfileAppLinking);
    }

    public static string[] GetCrossProfilePackages(DevicePolicyManager manager, ComponentName admin)
    {
        return manager.GetCrossProfilePackages(admin).ToArray();
    }

    public static bool TryEnableSystemApp(
        DevicePolicyManager manager,
        ComponentName admin,
        string packageName,
        string logTag,
        out string? error)
    {
        try
        {
            manager.EnableSystemApp(admin, packageName);
            return TrySetApplicationHidden(manager, admin, packageName, false, logTag, out error);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to enable system app {packageName}: {exception}");
            error = $"Android не смог включить системное приложение {packageName}.";
            return false;
        }
    }

    public static bool TrySetApplicationHidden(
        DevicePolicyManager manager,
        ComponentName admin,
        string packageName,
        bool hidden,
        string logTag,
        out string? error)
    {
        try
        {
            var hiddenBefore = TryReadApplicationHidden(manager, admin, packageName, hidden, logTag);

            var hiddenApplied = ApplicationHiddenPolicy.Apply(
                hidden,
                () => manager.IsApplicationHidden(admin, packageName),
                value => manager.SetApplicationHidden(admin, packageName, value),
                repairUnchangedPolicy: AndroidApiLevel.IsAtLeastUpsideDownCake());
            var currentHidden = manager.IsApplicationHidden(admin, packageName);
            Log.Debug(logTag,
                $"SetApplicationHidden result. package={packageName}, requestedHidden={hidden}, returned={hiddenApplied}, hiddenBefore={hiddenBefore?.ToString() ?? "<unknown>"}, currentHidden={currentHidden}, adminPackage={admin.PackageName}.");
            if (currentHidden != hidden)
            {
                Log.Warn(logTag,
                    $"SetApplicationHidden rejected. package={packageName}, requestedHidden={hidden}, returned={hiddenApplied}, hiddenBefore={hiddenBefore?.ToString() ?? "<unknown>"}, currentHidden={currentHidden}, adminPackage={admin.PackageName}.");
                error = GetSetApplicationHiddenFailureMessage(packageName, hidden);
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag,
                $"Failed to change hidden state. package={packageName}, requestedHidden={hidden}, adminPackage={admin.PackageName}, exception={exception.GetType().FullName}: {exception}");
            error = GetSetApplicationHiddenFailureMessage(packageName, hidden);
            return false;
        }
    }

    private static bool? TryReadApplicationHidden(
        DevicePolicyManager manager,
        ComponentName admin,
        string packageName,
        bool requestedHidden,
        string logTag)
    {
        try
        {
            return manager.IsApplicationHidden(admin, packageName);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag,
                $"Could not read hidden state before SetApplicationHidden. package={packageName}, requestedHidden={requestedHidden}, exception={exception.GetType().FullName}: {exception.Message}");
            return null;
        }
    }

    private static string GetSetApplicationHiddenFailureMessage(string packageName, bool hidden)
    {
        return hidden
            ? $"Android не смог скрыть {packageName}."
            : $"Android не смог восстановить {packageName}.";
    }

    public static bool TrySetCrossProfilePackages(
        DevicePolicyManager manager,
        ComponentName admin,
        ICollection<string> packages,
        string logTag)
    {
        try
        {
            manager.SetCrossProfilePackages(admin, packages);
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to change cross-profile package policy: {exception}");
            return false;
        }
    }

    public static Task<(bool Succeeded, string? Error)> TryDenyRuntimePermissionAsync(
        DevicePolicyManager manager,
        PackageManager? packageManager,
        ComponentName admin,
        string packageName,
        string permission,
        string logTag,
        CancellationToken cancellationToken = default)
        => TrySetRuntimePermissionPolicyAsync(manager, admin, packageName, permission,
            PermissionGrantState.Denied, logTag, cancellationToken);

    public static async Task<(bool Succeeded, string? Error)> TrySetRuntimePermissionPolicyAsync(
        DevicePolicyManager manager,
        ComponentName admin,
        string packageName,
        string permission,
        PermissionGrantState desiredState,
        string logTag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (desiredState is not (PermissionGrantState.Denied or PermissionGrantState.Default))
                throw new ArgumentOutOfRangeException(nameof(desiredState));
            var applied = manager.SetPermissionGrantState(
                admin,
                packageName,
                permission,
                desiredState);
            if (!applied)
                return (false, $"Android не разрешает менять {permission} у этого приложения. Разрешение может быть закреплено системой.");

            var confirmation = await WaitForRuntimePermissionRevokeConfirmationAsync(
                    manager,
                    admin,
                    packageName,
                    permission,
                    desiredState,
                    logTag,
                    cancellationToken)
                .ConfigureAwait(false);
            if (confirmation.Confirmed)
            {
                return (true, null);
            }

            var error = $"Android не подтвердил изменение политики {permission} у {packageName}.";
            Log.Warn(
                logTag,
                $"Permission revoke was not confirmed. package={packageName}, permission={permission}, state={confirmation.PolicyState}.");
            return (false, error);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            var error = $"Android не разрешил изменить политику {permission} у {packageName}.";
            Log.Warn(
                logTag,
                $"Failed to deny runtime permission. package={packageName}, permission={permission}, exception={exception.GetType().FullName}: {exception}");
            return (false, error);
        }
    }

    public static async Task<(bool Succeeded, string? Error)> TryRevokeRuntimePermissionOnceAsync(
        DevicePolicyManager manager,
        PackageManager packageManager,
        ComponentName admin,
        string packageName,
        string permission,
        string logTag,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (packageManager.CheckPermission(permission, packageName) != Permission.Granted)
                return (false, "Разрешение уже не выдано. Обновите список разрешений.");
            if (manager.GetPermissionGrantState(admin, packageName, permission) == PermissionGrantState.Denied)
                return (false, "Разрешение уже запрещено политикой. Сначала снимите запрет.");

            // After the first policy write, finish the pair even if the UI request is cancelled.
            var denied = await TrySetRuntimePermissionPolicyAsync(manager, admin, packageName, permission,
                PermissionGrantState.Denied, logTag, CancellationToken.None).ConfigureAwait(false);
            var grantRemoved = denied.Succeeded && await WaitForRuntimePermissionDeniedAsync(
                packageManager, packageName, permission).ConfigureAwait(false);

            var shouldClear = denied.Succeeded
                || manager.GetPermissionGrantState(admin, packageName, permission) == PermissionGrantState.Denied;
            if (!shouldClear)
                return (false, denied.Error ?? "Android не смог отозвать разрешение.");

            var cleared = await TrySetRuntimePermissionPolicyAsync(manager, admin, packageName, permission,
                PermissionGrantState.Default, logTag, CancellationToken.None).ConfigureAwait(false);
            if (!cleared.Succeeded)
                return (false, $"{cleared.Error} Временный запрет мог остаться; нажмите «Снять запрет».");

            var remainsRevoked = await WaitForRuntimePermissionDeniedAsync(
                packageManager, packageName, permission).ConfigureAwait(false);
            if (!denied.Succeeded || !grantRemoved || !remainsRevoked)
                return (false, "Android не подтвердил отзыв доступа без постоянного запрета. Обновите список разрешений.");
            return (true, null);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag,
                $"Failed to revoke runtime permission without policy. package={packageName}, permission={permission}, exception={exception}");
            return (false, "Android не смог завершить отзыв доступа. Обновите список разрешений; если появился статус «Запрещено», нажмите «Снять запрет».");
        }
    }

    private static async Task<bool> WaitForRuntimePermissionDeniedAsync(
        PackageManager packageManager, string packageName, string permission)
    {
        for (var attempt = 1; attempt <= RuntimePermissionRevokeConfirmationAttempts; attempt++)
        {
            if (packageManager.CheckPermission(permission, packageName) != Permission.Granted)
                return true;
            if (attempt < RuntimePermissionRevokeConfirmationAttempts)
                await Task.Delay(RuntimePermissionRevokeConfirmationDelayMilliseconds).ConfigureAwait(false);
        }
        return false;
    }

    private static async Task<RuntimePermissionRevokeConfirmation> WaitForRuntimePermissionRevokeConfirmationAsync(
        DevicePolicyManager manager,
        ComponentName admin,
        string packageName,
        string permission,
        PermissionGrantState desiredState,
        string logTag,
        CancellationToken cancellationToken)
    {
        var currentState = PermissionGrantState.Default;
        for (var attempt = 1; attempt <= RuntimePermissionRevokeConfirmationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentState = manager.GetPermissionGrantState(admin, packageName, permission);
            if (currentState == desiredState)
            {
                if (attempt > 1)
                    Log.Debug(
                        logTag,
                        $"Permission revoke confirmed after retry. package={packageName}, permission={permission}, attempt={attempt}, state={currentState}.");

                return new RuntimePermissionRevokeConfirmation(true, currentState);
            }

            if (attempt < RuntimePermissionRevokeConfirmationAttempts)
                await Task.Delay(RuntimePermissionRevokeConfirmationDelayMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
        }

        return new RuntimePermissionRevokeConfirmation(false, currentState);
    }

    private readonly record struct RuntimePermissionRevokeConfirmation(
        bool Confirmed,
        PermissionGrantState PolicyState);

    [UnsupportedOSPlatform("android34.0")]
    private static void SetCrossProfileContactsSearchDisabled(DevicePolicyManager manager, ComponentName admin,
        bool disabled)
    {
        manager.SetCrossProfileContactsSearchDisabled(admin, disabled);
    }

    [SupportedOSPlatform("android34.0")]
    private static void SetManagedProfileContactsAccessPolicy(DevicePolicyManager manager, bool disabled)
    {
        manager.ManagedProfileContactsAccessPolicy = new PackagePolicy(
            disabled
                ? PackagePolicyMode.Allowlist
                : PackagePolicyMode.Blocklist);
    }
}
