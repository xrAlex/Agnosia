using System.Runtime.Versioning;
using Agnosia.Android.Api.Internal;
using Android.App.Admin;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

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

    public static void AddParentProfileAppLinking(DevicePolicyManager manager, ComponentName admin) =>
        manager.AddUserRestriction(admin, UserManager.AllowParentProfileAppLinking);

    public static string[] GetCrossProfilePackages(DevicePolicyManager manager, ComponentName admin) =>
        AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(manager.GetCrossProfilePackages(admin));

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
            var hiddenApplied = manager.SetApplicationHidden(admin, packageName, hidden);
            var currentHidden = manager.IsApplicationHidden(admin, packageName);
            Log.Info(logTag, $"SetApplicationHidden result. package={packageName}, requestedHidden={hidden}, returned={hiddenApplied}, currentHidden={currentHidden}.");
            if (!hiddenApplied && currentHidden != hidden)
            {
                error = hidden
                    ? $"Android не смог скрыть {packageName}."
                    : $"Android не смог восстановить {packageName}.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to change hidden state for {packageName}: {exception}");
            error = hidden
                ? $"Android не смог скрыть {packageName}."
                : $"Android не смог восстановить {packageName}.";
            return false;
        }
    }

    public static bool TrySetCrossProfilePackages(
        DevicePolicyManager manager,
        ComponentName admin,
        ICollection<string> packages,
        string logTag)
    {
        try
        {
            manager.SetCrossProfilePackages(admin, AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(packages));
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
            {
                return true;
            }

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
    private static void SetCrossProfileContactsSearchDisabled(DevicePolicyManager manager, ComponentName admin, bool disabled) =>
        manager.SetCrossProfileContactsSearchDisabled(admin, disabled);

    [SupportedOSPlatform("android34.0")]
    private static void SetManagedProfileContactsAccessPolicy(DevicePolicyManager manager, bool disabled) =>
        manager.ManagedProfileContactsAccessPolicy = new PackagePolicy(
            disabled
                ? PackagePolicyMode.Allowlist
                : PackagePolicyMode.Blocklist);
}
