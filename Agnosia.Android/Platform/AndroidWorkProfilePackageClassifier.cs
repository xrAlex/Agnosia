using Android.Content.PM;
using Agnosia.Android.Api.Platform;

namespace Agnosia.Android.Platform;

public static class AndroidWorkProfilePackageClassifier
{
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

    public static bool IsSystemApp(ApplicationInfo app)
    {
        return (app.Flags & ApplicationInfoFlags.System) != 0
               || (app.Flags & ApplicationInfoFlags.UpdatedSystemApp) != 0
               || IsSystemPath(app.SourceDir)
               || IsSystemPath(app.PublicSourceDir);
    }

    public static bool IsSystemPackage(PackageManager? packageManager, string? packageName)
    {
        if (packageManager is null || string.IsNullOrWhiteSpace(packageName)) return false;

        try
        {
            var app = packageManager.GetApplicationInfo(
                packageName,
                AndroidSystemApi.GetInstalledApplicationFlags() | PackageInfoFlags.MatchDisabledComponents);
            return IsSystemApp(app);
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    private static bool IsSystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        foreach (var prefix in SystemPathPrefixes)
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return true;

        return false;
    }
}
