using Agnosia.Android.Api.Platform;
using Agnosia.Android.Platform;
using Agnosia.Android.Storage;
using Agnosia.Models;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

internal static class LockdownVpnController
{
    private const string LogTag = "AgnosiaLockdown";

    public static OperationResult SetEnabled(
        Context context,
        DevicePolicyManager manager,
        ComponentName admin,
        bool enabled)
    {
        var wasEnabled = LockdownSettingsStore.IsEnabled();
        LockdownSettingsStore.SetEnabled(enabled);
        var targetPackage = enabled ? context.PackageName : null;
        var directNetworkAllowlist = enabled ? CreateDirectNetworkPackageAllowlist(context) : null;

        try
        {
            var policyAlreadyApplied = IsAlwaysOnVpnPolicyApplied(
                manager,
                admin,
                targetPackage,
                lockdownEnabled: enabled,
                directNetworkAllowlist);
            if (!policyAlreadyApplied)
            {
                if (enabled)
                    manager.SetAlwaysOnVpnPackage(admin, targetPackage, enabled, directNetworkAllowlist);
                else
                    manager.SetAlwaysOnVpnPackage(admin, null, enabled);

                Log.Info(LogTag, $"Lockdown always-on VPN policy changed. enabled={enabled}.");
            }
            else
            {
                Log.Debug(LogTag, $"Lockdown always-on VPN policy already applied. enabled={enabled}.");
            }

            if (enabled)
                LockdownVpnService.StartOrRefresh(context);
            else
                LockdownVpnService.Stop(context);

            return OperationResult.Success(enabled ? "Lockdown включён." : "Lockdown выключен.");
        }
        catch (PackageManager.NameNotFoundException exception)
        {
            LockdownSettingsStore.SetEnabled(wasEnabled);
            Log.Warn(LogTag, $"Lockdown VPN package is not installed in this profile: {exception.Message}");
            return OperationResult.Failure("Android не видит VPN-службу Agnosia в рабочем профиле.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            LockdownSettingsStore.SetEnabled(wasEnabled);
            Log.Warn(LogTag, $"Failed to change Lockdown always-on VPN policy: {exception}");
            return OperationResult.Failure(enabled
                ? "Android не смог включить always-on Lockdown VPN."
                : "Android не смог выключить always-on Lockdown VPN.");
        }
    }

    public static OperationResult RefreshPolicy(Context context, DevicePolicyManager manager, ComponentName admin)
    {
        if (!LockdownSettingsStore.IsEnabled())
            return OperationResult.Success("Lockdown выключен.");

        var directNetworkAllowlist = CreateDirectNetworkPackageAllowlist(context);
        try
        {
            manager.SetAlwaysOnVpnPackage(admin, context.PackageName, true, directNetworkAllowlist);
        }
        catch (PackageManager.NameNotFoundException exception)
        {
            Log.Warn(LogTag, $"Lockdown direct-network allowlist contains a missing package: {exception.Message}");
            return OperationResult.Failure("Android не смог обновить список приложений Lockdown.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to refresh Lockdown always-on VPN policy: {exception}");
            return OperationResult.Failure("Android не смог обновить always-on Lockdown VPN.");
        }

        LockdownVpnService.StartOrRefresh(context);
        return OperationResult.Success("Lockdown обновлён.");
    }

    public static void EnsureEnabledPolicy(Context context, Type adminReceiverType)
    {
        if (!LockdownSettingsStore.IsEnabled()) return;

        var manager = AndroidSystemApi.GetDevicePolicyManager(context);
        if (manager is null)
        {
            Log.Warn(LogTag, "DevicePolicyManager unavailable; cannot enforce Lockdown VPN policy.");
            return;
        }

        var admin = AgnosiaUtilities.GetAdminComponent(context, adminReceiverType);
        var directNetworkAllowlist = CreateDirectNetworkPackageAllowlist(context);
        if (IsAlwaysOnVpnPolicyApplied(
                manager,
                admin,
                context.PackageName,
                lockdownEnabled: true,
                directNetworkAllowlist))
        {
            Log.Debug(LogTag, "Lockdown always-on VPN policy already enforced; skipping startup refresh.");
            return;
        }

        var result = SetEnabled(context, manager, admin, true);
        if (!result.Succeeded)
            Log.Warn(LogTag, $"Failed to enforce Lockdown VPN policy on startup: {result.Message}");
    }

    private static bool IsAlwaysOnVpnPolicyApplied(
        DevicePolicyManager manager,
        ComponentName admin,
        string? packageName,
        bool lockdownEnabled,
        ICollection<string>? directNetworkAllowlist)
    {
        try
        {
            var alwaysOnPackage = manager.GetAlwaysOnVpnPackage(admin);
            if (!string.Equals(alwaysOnPackage, packageName, StringComparison.Ordinal))
                return false;

            return packageName is null
                   || manager.IsAlwaysOnVpnLockdownEnabled(admin) == lockdownEnabled
                   && IsLockdownAllowlistApplied(manager, admin, directNetworkAllowlist);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to read Lockdown always-on VPN policy: {exception}");
            return false;
        }
    }

    internal static string[] CreateDirectNetworkPackageAllowlist(Context context)
    {
        var blockedPackages = LockdownSettingsStore
            .LoadBlockedPackages()
            .ToHashSet(StringComparer.Ordinal);

        return EnumerateInstalledNonSystemPackages(context)
            .Where(packageName => !blockedPackages.Contains(packageName))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsLockdownAllowlistApplied(
        DevicePolicyManager manager,
        ComponentName admin,
        ICollection<string>? expectedAllowlist)
    {
        var actualAllowlist = manager.GetAlwaysOnVpnLockdownWhitelist(admin);
        var actual = actualAllowlist?.ToHashSet(StringComparer.Ordinal) ?? [];
        var expected = expectedAllowlist?.ToHashSet(StringComparer.Ordinal) ?? [];
        return actual.SetEquals(expected);
    }

    private static IEnumerable<string> EnumerateInstalledNonSystemPackages(Context context)
    {
        if (context.PackageManager is not { } packageManager) yield break;

        foreach (var packageInfo in packageManager.GetInstalledPackages(PackageInfoFlags.MatchDisabledComponents))
        {
            if (!IsInstalled(packageInfo)) continue;

            var packageName = packageInfo.PackageName;
            if (string.IsNullOrWhiteSpace(packageName)
                || string.Equals(packageName, context.PackageName, StringComparison.Ordinal)
                || IsSystemPackage(packageInfo))
            {
                continue;
            }

            yield return packageName;
        }
    }

    private static bool IsInstalled(PackageInfo packageInfo)
    {
        var flags = packageInfo.ApplicationInfo?.Flags ?? 0;
        return (flags & ApplicationInfoFlags.Installed) != 0;
    }

    private static bool IsSystemPackage(PackageInfo packageInfo)
    {
        var flags = packageInfo.ApplicationInfo?.Flags ?? 0;
        return (flags & (ApplicationInfoFlags.System | ApplicationInfoFlags.UpdatedSystemApp)) != 0;
    }
}
