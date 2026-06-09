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

            var hiddenApplied = manager.SetApplicationHidden(admin, packageName, hidden);
            var currentHidden = manager.IsApplicationHidden(admin, packageName);
            Log.Debug(logTag,
                $"SetApplicationHidden result. package={packageName}, requestedHidden={hidden}, returned={hiddenApplied}, hiddenBefore={hiddenBefore?.ToString() ?? "<unknown>"}, currentHidden={currentHidden}, adminPackage={admin.PackageName}.");
            if (!hiddenApplied && currentHidden != hidden)
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

    public static async Task<(bool Succeeded, string? Error)> TryDenyRuntimePermissionAsync(
        DevicePolicyManager manager,
        PackageManager? packageManager,
        ComponentName admin,
        string packageName,
        string permission,
        string logTag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            manager.SetPermissionGrantState(
                admin,
                packageName,
                permission,
                PermissionGrantState.Denied);

            var confirmation = await WaitForRuntimePermissionRevokeConfirmationAsync(
                    manager,
                    packageManager,
                    admin,
                    packageName,
                    permission,
                    logTag,
                    cancellationToken)
                .ConfigureAwait(false);
            if (confirmation.Confirmed)
            {
                if (confirmation.PolicyState != PermissionGrantState.Denied)
                    Log.Debug(
                        logTag,
                        $"Permission is already denied by package state. package={packageName}, permission={permission}, policyState={confirmation.PolicyState}.");

                return (true, null);
            }

            var error = $"Android не подтвердил отзыв {permission} у {packageName}.";
            Log.Warn(
                logTag,
                $"Permission revoke was not confirmed. package={packageName}, permission={permission}, state={confirmation.PolicyState}.");
            return (false, error);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            var error = $"Android не смог отозвать {permission} у {packageName}.";
            Log.Warn(
                logTag,
                $"Failed to deny runtime permission. package={packageName}, permission={permission}, exception={exception.GetType().FullName}: {exception}");
            return (false, error);
        }
    }

    private static async Task<RuntimePermissionRevokeConfirmation> WaitForRuntimePermissionRevokeConfirmationAsync(
        DevicePolicyManager manager,
        PackageManager? packageManager,
        ComponentName admin,
        string packageName,
        string permission,
        string logTag,
        CancellationToken cancellationToken)
    {
        var currentState = PermissionGrantState.Default;
        for (var attempt = 1; attempt <= RuntimePermissionRevokeConfirmationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentState = manager.GetPermissionGrantState(admin, packageName, permission);
            if (currentState == PermissionGrantState.Denied
                || IsPermissionCurrentlyDenied(packageManager, packageName, permission, logTag))
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

    private static bool IsPermissionCurrentlyDenied(
        PackageManager? packageManager,
        string packageName,
        string permission,
        string logTag)
    {
        if (packageManager is null) return false;

        try
        {
            return packageManager.CheckPermission(permission, packageName) != Permission.Granted;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            Log.Debug(
                logTag,
                $"Could not check current permission grant. package={packageName}, permission={permission}, exception={exception.GetType().FullName}: {exception.Message}");
            return false;
        }
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
