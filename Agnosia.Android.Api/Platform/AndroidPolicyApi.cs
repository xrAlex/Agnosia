using System.Runtime.Versioning;
using Agnosia.Android.Api.Internal;
using Android.App.Admin;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Platform;

public static class AndroidPolicyApi
{
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
        return AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(manager.GetCrossProfilePackages(admin));
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

    public static bool TryInstallExistingPackage(
        DevicePolicyManager manager,
        ComponentName admin,
        string packageName,
        string logTag,
        out string? error)
    {
        try
        {
            if (manager.InstallExistingPackage(admin, packageName))
            {
                error = null;
                return true;
            }

            error = $"Android не нашел установленный пакет {packageName} в другом профиле.";
            return false;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to install existing package {packageName}: {exception}");
            error = $"Android не смог установить существующее приложение {packageName} в этом профиле.";
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
            manager.SetCrossProfilePackages(
                admin,
                AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(packages));
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to change cross-profile package policy: {exception}");
            return false;
        }
    }

    public static bool TryEnsureRequiredCrossProfilePackages(
        DevicePolicyManager manager,
        ComponentName admin,
        string logTag)
    {
        try
        {
            var current = manager.GetCrossProfilePackages(admin).ToArray();
            var required = AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(current);
            if (current.Length == required.Length
                && current.ToHashSet(StringComparer.Ordinal).SetEquals(required))
                return true;

            manager.SetCrossProfilePackages(admin, required);
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to enforce required cross-profile package policy: {exception}");
            return false;
        }
    }

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
