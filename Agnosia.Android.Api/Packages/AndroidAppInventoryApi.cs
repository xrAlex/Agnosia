using System.Security.Cryptography;
using System.Text;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Path = System.IO.Path;

namespace Agnosia.Android.Api;

public static class AndroidAppInventoryApi
{
    private const int AppIconSizePixels = 48;
    private const int MaxIconCacheEntries = 512;
    private const string IconCacheDirectoryName = "app-icons";
    private const string IconCacheFileExtension = ".png";
    private const string MissingIconCacheFileExtension = ".missing";
    private static readonly Lock IconCacheSync = new();
    private static readonly Dictionary<string, AppIconCacheEntry> IconCache = new(StringComparer.Ordinal);

    public static List<AppServiceModel> QueryInstalledApps(
        Context context,
        PackageManager packageManager,
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        bool showAll,
        CancellationToken cancellationToken = default)
    {
        var apps = packageManager.GetInstalledApplications(AndroidSystemApi.GetInstalledApplicationFlags());
        var models = new List<AppServiceModel>(apps.Count);
        var installedPackageNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(app.PackageName))
            {
                installedPackageNames.Add(app.PackageName);
            }

            if (TryCreateModel(context, packageManager, policyManager, admin, app, showAll) is { } model)
            {
                models.Add(model);
            }
        }

        PruneMemoryIconCache(installedPackageNames);
        models.Sort(static (left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Label, right.Label));
        return models;
    }

    public static byte[]? LoadAppIconPng(
        Context context,
        PackageManager packageManager,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(packageName)
            || string.Equals(packageName, context.PackageName, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var app = TryGetApplicationInfo(packageManager, packageName, AndroidSystemApi.GetInstalledApplicationFlags())
                ?? packageManager.GetApplicationInfo(packageName, PackageInfoFlags.MatchDisabledComponents);
            cancellationToken.ThrowIfCancellationRequested();
            return ResolveAppIconPng(context, packageManager, app, packageName, cancellationToken);
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
            or InvalidOperationException
            || AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static AppServiceModel? TryCreateModel(
        Context context,
        PackageManager packageManager,
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        ApplicationInfo app,
        bool showAll)
    {
        var packageName = app.PackageName;
        if (string.IsNullOrWhiteSpace(packageName)
            || string.Equals(packageName, context.PackageName, StringComparison.Ordinal))
        {
            return null;
        }

        var isSystem = AndroidWorkProfilePackageClassifier.IsSystemApp(app);
        var isInstalled = (app.Flags & ApplicationInfoFlags.Installed) != 0;
        var isHidden = TryIsApplicationHidden(policyManager, admin, packageName);
        if (!showAll && (isSystem || (!isInstalled && !isHidden)))
        {
            return null;
        }

        return new AppServiceModel
        {
            PackageName = packageName,
            Label = packageManager.GetApplicationLabel(app),
            SourceDirectory = app.SourceDir,
            SplitApks = app.SplitSourceDirs?.ToArray() ?? [],
            IsSystem = isSystem,
            IsHidden = isHidden,
            CanLaunch = packageManager.GetLaunchIntentForPackage(packageName) is not null,
            IsInstalled = isInstalled,
            IconPng = TryGetMemoryCachedIcon(packageManager, packageName, out var cachedIcon) ? cachedIcon : null
        };
    }

    private static bool TryIsApplicationHidden(
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        string packageName)
    {
        if (policyManager is null || admin is null)
        {
            return false;
        }

        try
        {
            return policyManager.IsApplicationHidden(admin, packageName);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    private static ApplicationInfo? TryGetApplicationInfo(
        PackageManager packageManager,
        string packageName,
        PackageInfoFlags flags)
    {
        try
        {
            return packageManager.GetApplicationInfo(packageName, flags);
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
            || AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static byte[]? ResolveAppIconPng(
        Context context,
        PackageManager packageManager,
        ApplicationInfo app,
        string packageName,
        CancellationToken cancellationToken)
    {
        var hasIdentity = TryGetPackageIdentity(packageManager, packageName, out var identity);
        if (hasIdentity && TryGetCachedIcon(context, packageName, identity, out var cachedIcon))
        {
            return cachedIcon;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var iconPng = TryRenderAppIcon(context, packageManager, app, packageName);
        if (hasIdentity)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CacheIcon(context, packageName, identity, iconPng);
        }

        return iconPng;
    }

    private static byte[]? TryRenderAppIcon(
        Context context,
        PackageManager packageManager,
        ApplicationInfo app,
        string packageName)
    {
        try
        {
            using var drawable = ResolveLauncherIcon(context, packageName) ?? packageManager.GetApplicationIcon(app);
            using var bitmap = RenderAppIcon(drawable);
            using var stream = new MemoryStream();
            bitmap.Compress(
                Bitmap.CompressFormat.Png ?? throw new InvalidOperationException("PNG compress format is unavailable."),
                100,
                stream);
            return stream.ToArray();
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static Drawable? ResolveLauncherIcon(Context context, string packageName)
    {
        if (context.GetSystemService(Context.LauncherAppsService) is not LauncherApps launcherApps)
        {
            return null;
        }

        try
        {
            var activities = launcherApps.GetActivityList(packageName, Process.MyUserHandle());
            return activities?.FirstOrDefault()?.GetIcon(0);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static bool TryGetPackageIdentity(
        PackageManager packageManager,
        string packageName,
        out PackageIdentity identity)
    {
        try
        {
            var packageInfo = TryGetPackageInfo(packageManager, packageName, AndroidSystemApi.GetInstalledApplicationFlags())
                ?? packageManager.GetPackageInfo(packageName, 0);
            if (packageInfo is null)
            {
                identity = default;
                return false;
            }

            identity = new PackageIdentity(packageInfo.LongVersionCode, packageInfo.LastUpdateTime);
            return true;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
            || AndroidRecoverableException.IsMatch(exception))
        {
            identity = default;
            return false;
        }
    }

    private static PackageInfo? TryGetPackageInfo(
        PackageManager packageManager,
        string packageName,
        PackageInfoFlags flags)
    {
        try
        {
            return packageManager.GetPackageInfo(packageName, flags);
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
            || AndroidRecoverableException.IsMatch(exception))
        {
            return null;
        }
    }

    private static bool TryGetCachedIcon(
        Context context,
        string packageName,
        PackageIdentity identity,
        out byte[]? iconPng)
    {
        lock (IconCacheSync)
        {
            if (IconCache.TryGetValue(packageName, out var entry) && entry.Identity == identity)
            {
                if (entry.IconPng is { Length: > 0 })
                {
                    iconPng = entry.IconPng;
                    return true;
                }

                IconCache.Remove(packageName);
                iconPng = null;
                return false;
            }
        }

        var cacheKey = GetIconCacheKey(packageName, identity);
        if (TryReadDiskCachedIcon(context, cacheKey, out iconPng))
        {
            lock (IconCacheSync)
            {
                IconCache[packageName] = new AppIconCacheEntry(identity, iconPng);
            }

            return true;
        }

        return false;
    }

    private static bool TryGetMemoryCachedIcon(
        PackageManager packageManager,
        string packageName,
        out byte[]? iconPng)
    {
        iconPng = null;
        if (!TryGetPackageIdentity(packageManager, packageName, out var identity))
        {
            return false;
        }

        lock (IconCacheSync)
        {
            if (IconCache.TryGetValue(packageName, out var entry) && entry.Identity == identity)
            {
                if (entry.IconPng is { Length: > 0 })
                {
                    iconPng = entry.IconPng;
                    return true;
                }

                IconCache.Remove(packageName);
                return false;
            }
        }

        return false;
    }

    private static bool TryReadDiskCachedIcon(Context context, string cacheKey, out byte[]? iconPng)
    {
        iconPng = null;
        var directory = GetIconCacheDirectory(context);
        if (directory is null)
        {
            return false;
        }

        var iconPath = Path.Combine(directory, cacheKey + IconCacheFileExtension);
        if (File.Exists(iconPath))
        {
            try
            {
                iconPng = File.ReadAllBytes(iconPath);
                return iconPng.Length > 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        var missingPath = Path.Combine(directory, cacheKey + MissingIconCacheFileExtension);
        if (File.Exists(missingPath))
        {
            try
            {
                File.Delete(missingPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    private static void CacheIcon(
        Context context,
        string packageName,
        PackageIdentity identity,
        byte[]? iconPng)
    {
        if (iconPng is not { Length: > 0 })
        {
            lock (IconCacheSync)
            {
                IconCache.Remove(packageName);
            }

            WriteDiskCachedIcon(context, GetIconCacheKey(packageName, identity), null);
            return;
        }

        lock (IconCacheSync)
        {
            IconCache[packageName] = new AppIconCacheEntry(identity, iconPng);
        }

        WriteDiskCachedIcon(context, GetIconCacheKey(packageName, identity), iconPng);
    }

    private static void WriteDiskCachedIcon(Context context, string cacheKey, byte[]? iconPng)
    {
        var directory = GetOrCreateIconCacheDirectory(context);
        if (directory is null)
        {
            return;
        }

        try
        {
            var iconPath = Path.Combine(directory, cacheKey + IconCacheFileExtension);
            var missingPath = Path.Combine(directory, cacheKey + MissingIconCacheFileExtension);
            if (iconPng is { Length: > 0 })
            {
                File.WriteAllBytes(iconPath, iconPng);
                File.Delete(missingPath);
                return;
            }

            File.Delete(missingPath);
            File.Delete(iconPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void PruneMemoryIconCache(HashSet<string> installedPackageNames)
    {
        lock (IconCacheSync)
        {
            foreach (var packageName in IconCache.Keys.Where(packageName => !installedPackageNames.Contains(packageName)).ToArray())
            {
                IconCache.Remove(packageName);
            }

            foreach (var packageName in IconCache.Keys.Take(Math.Max(0, IconCache.Count - MaxIconCacheEntries)).ToArray())
            {
                IconCache.Remove(packageName);
            }
        }
    }

    private static void PruneDiskIconCache(Context context)
    {
        var directory = GetIconCacheDirectory(context);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(directory)
                .Select(path => new FileInfo(path))
                .Where(file => string.Equals(file.Extension, IconCacheFileExtension, StringComparison.Ordinal)
                    || string.Equals(file.Extension, MissingIconCacheFileExtension, StringComparison.Ordinal))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(MaxIconCacheEntries)
                .ToArray();

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Bitmap RenderAppIcon(Drawable drawable)
    {
        if (drawable is BitmapDrawable { Bitmap: { } existingBitmap })
        {
            return Bitmap.CreateScaledBitmap(existingBitmap, AppIconSizePixels, AppIconSizePixels, true)
                ?? throw new InvalidOperationException("Android could not scale the app icon.");
        }

        var bitmap = Bitmap.CreateBitmap(
            AppIconSizePixels,
            AppIconSizePixels,
            Bitmap.Config.Argb8888 ?? throw new InvalidOperationException("ARGB8888 bitmap config is unavailable."))
            ?? throw new InvalidOperationException("Android could not allocate the app icon bitmap.");

        using var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
        drawable.Draw(canvas);
        return bitmap;
    }

    private static string? GetIconCacheDirectory(Context context) =>
        context.CacheDir?.AbsolutePath is { Length: > 0 } cacheRoot
            ? Path.Combine(cacheRoot, IconCacheDirectoryName)
            : null;

    private static string? GetOrCreateIconCacheDirectory(Context context)
    {
        var directory = GetIconCacheDirectory(context);
        if (directory is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetIconCacheKey(string packageName, PackageIdentity identity)
    {
        var packageHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(packageName)));
        return $"{packageHash}.{identity.VersionCode}.{identity.LastUpdateTime}";
    }

    private readonly record struct PackageIdentity(long VersionCode, long LastUpdateTime);

    private sealed record AppIconCacheEntry(PackageIdentity Identity, byte[]? IconPng);
}
