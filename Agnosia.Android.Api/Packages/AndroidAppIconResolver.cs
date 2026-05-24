using System.Security.Cryptography;
using System.Text;
using Agnosia.Android.Api.Platform;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Path = System.IO.Path;

namespace Agnosia.Android.Api.Packages;

internal static class AndroidAppIconResolver
{
    private const int AppIconSizePixels = 48;
    private const int MaxIconCacheEntries = 512;
    private const string IconCacheDirectoryName = "app-icons";
    private const string IconCacheFileExtension = ".png";
    private const string MissingIconCacheFileExtension = ".missing";

    private static readonly Lock IconCacheSync = new();
    private static readonly Dictionary<string, AppIconCacheEntry> IconCache = new(StringComparer.Ordinal);

    public static byte[]? ResolveAppIconPng(
        Context context,
        PackageManager packageManager,
        ApplicationInfo app,
        string packageName,
        PackageIdentity identity,
        CancellationToken cancellationToken)
    {
        if (TryLoadCachedAppIconPng(context, packageName, identity, out var cachedIcon)) return cachedIcon;

        cancellationToken.ThrowIfCancellationRequested();
        var iconPng = TryRenderAppIcon(context, packageManager, app, packageName);

        cancellationToken.ThrowIfCancellationRequested();
        CacheIcon(context, packageName, identity, iconPng);

        return iconPng;
    }

    public static bool TryLoadCachedAppIconPng(
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

    public static void PruneMemoryIconCache(HashSet<string> installedPackageNames)
    {
        lock (IconCacheSync)
        {
            List<string>? stalePackages = null;
            foreach (var packageName in IconCache.Keys)
            {
                if (installedPackageNames.Contains(packageName)) continue;

                stalePackages ??= [];
                stalePackages.Add(packageName);
            }

            if (stalePackages is not null)
            {
                foreach (var packageName in stalePackages) IconCache.Remove(packageName);
            }

            var overflowCount = IconCache.Count - MaxIconCacheEntries;
            if (overflowCount <= 0) return;

            var overflowPackages = new List<string>(overflowCount);
            foreach (var packageName in IconCache.Keys)
            {
                overflowPackages.Add(packageName);
                if (overflowPackages.Count >= overflowCount) break;
            }

            foreach (var packageName in overflowPackages) IconCache.Remove(packageName);
        }
    }

    private static Drawable? TryGetApplicationIcon(PackageManager packageManager, ApplicationInfo app)
    {
        try
        {
            return packageManager.GetApplicationIcon(app);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryRenderAppIcon(
        Context context,
        PackageManager packageManager,
        ApplicationInfo app,
        string packageName)
    {
        try
        {
            using var drawable = TryGetApplicationIcon(packageManager, app) ?? ResolveLauncherIcon(context, packageName);
            if (drawable is null) return null;

            using var bitmap = RenderAppIcon(drawable);
            using var stream = new MemoryStream();
            bitmap.Compress(
                Bitmap.CompressFormat.Png ?? throw new InvalidOperationException("PNG compress format is unavailable."),
                85,
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
        if (context.GetSystemService(Context.LauncherAppsService) is not LauncherApps launcherApps) return null;

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

    private static bool TryReadDiskCachedIcon(Context context, string cacheKey, out byte[]? iconPng)
    {
        iconPng = null;
        var directory = GetIconCacheDirectory(context);
        if (directory is null) return false;

        var iconPath = Path.Combine(directory, cacheKey + IconCacheFileExtension);
        if (File.Exists(iconPath))
            try
            {
                iconPng = File.ReadAllBytes(iconPath);
                return iconPng.Length > 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }

        var missingPath = Path.Combine(directory, cacheKey + MissingIconCacheFileExtension);
        if (File.Exists(missingPath))
            try
            {
                File.Delete(missingPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
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
        if (directory is null) return;

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

    private static Bitmap RenderAppIcon(Drawable drawable)
    {
        if (drawable is BitmapDrawable { Bitmap: { } existingBitmap })
            return Bitmap.CreateScaledBitmap(existingBitmap, AppIconSizePixels, AppIconSizePixels, true)
                   ?? throw new InvalidOperationException("Android could not scale the app icon.");

        var bitmap = Bitmap.CreateBitmap(
                         AppIconSizePixels,
                         AppIconSizePixels,
                         Bitmap.Config.Argb8888 ??
                         throw new InvalidOperationException("ARGB8888 bitmap config is unavailable."))
                     ?? throw new InvalidOperationException("Android could not allocate the app icon bitmap.");

        using var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, canvas.Width, canvas.Height);
        drawable.Draw(canvas);
        return bitmap;
    }

    private static string? GetIconCacheDirectory(Context context)
    {
        return context.CacheDir?.AbsolutePath is { Length: > 0 } cacheRoot
            ? Path.Combine(cacheRoot, IconCacheDirectoryName)
            : null;
    }

    private static string? GetOrCreateIconCacheDirectory(Context context)
    {
        var directory = GetIconCacheDirectory(context);
        if (directory is null) return null;

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
        var packageHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packageName))).Substring(0, 16);
        return $"{packageHash}.{identity.VersionCode}";
    }

    private sealed record AppIconCacheEntry(PackageIdentity Identity, byte[]? IconPng);
}
