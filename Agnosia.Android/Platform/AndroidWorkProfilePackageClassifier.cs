using Android.Content.PM;

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

    private static bool IsSystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        foreach (var prefix in SystemPathPrefixes)
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return true;

        return false;
    }
}