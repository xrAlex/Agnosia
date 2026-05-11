using Android.App.Admin;
using Android.Content;
using Android.Content.PM;

namespace Agnosia.Android.Api;

public static class AndroidWorkProfilePackageClassifier
{
    private static readonly HashSet<string> RequiredWorkProfileInfrastructurePackages = new(StringComparer.Ordinal)
    {
        "com.android.managedprovisioning",
        "com.google.android.apps.enterprise.dmagent",
        "com.google.android.apps.work.clouddpc"
    };

    private static readonly string[] SystemPathPrefixes =
    [
        "/apex/",
        "/odm/",
        "/oem/",
        "/product/",
        "/system/",
        "/system_ext/",
        "/vendor/"
    ];

    public static bool IsUserAppFreezeCandidate(
        Context context,
        PackageManager? packageManager,
        DevicePolicyManager policyManager,
        ApplicationInfo app,
        string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)
            || string.Equals(packageName, context.PackageName, StringComparison.Ordinal)
            || RequiredWorkProfileInfrastructurePackages.Contains(packageName))
        {
            return false;
        }

        if (policyManager.IsDeviceOwnerApp(packageName)
            || policyManager.IsProfileOwnerApp(packageName)
            || IsActiveDeviceAdmin(policyManager, packageName)
            || AndroidVpnAutomationApi.IsKnownVpnClientPackage(packageName))
        {
            return false;
        }

        if (!IsInstalled(app) || IsSystemApp(app))
        {
            return false;
        }

        return packageManager is null || HasLaunchIntent(packageManager, packageName);
    }

    public static bool IsSystemApp(ApplicationInfo app) =>
        (app.Flags & ApplicationInfoFlags.System) != 0
        || (app.Flags & ApplicationInfoFlags.UpdatedSystemApp) != 0
        || IsSystemPath(app.SourceDir)
        || IsSystemPath(app.PublicSourceDir);

    private static bool IsInstalled(ApplicationInfo app) =>
        (app.Flags & ApplicationInfoFlags.Installed) != 0;

    private static bool IsActiveDeviceAdmin(DevicePolicyManager policyManager, string packageName)
    {
        var activeAdmins = policyManager.ActiveAdmins;
        if (activeAdmins is null)
        {
            return false;
        }

        foreach (var admin in activeAdmins)
        {
            if (string.Equals(admin.PackageName, packageName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLaunchIntent(PackageManager packageManager, string packageName)
    {
        try
        {
            return packageManager.GetLaunchIntentForPackage(packageName) is not null;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    private static bool IsSystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var prefix in SystemPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
