using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidUri = Android.Net.Uri;
using JavaFile = Java.IO.File;
using JavaFileNotFoundException = Java.IO.FileNotFoundException;
using JavaIOException = Java.IO.IOException;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

public static class AndroidPackageApi
{
    private const string StaleApkMessage = "APK изменился или приложение было обновлено. Обновите список и повторите.";

    public static bool CanRequestInstalls(Activity activity, string logTag)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return true;
        }

        try
        {
            return activity.PackageManager?.CanRequestPackageInstalls() == true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to check package install access: {exception}");
            return false;
        }
    }

    public static bool TryOpenUnknownSourcesSettings(Activity activity, string logTag, Action<string> onError)
    {
        var intent = new Intent(Settings.ActionManageUnknownAppSources);
        intent.SetData(AndroidUri.Parse($"package:{activity.PackageName}"));
        if (AndroidIntentApi.TryStartActivity(
            activity,
            intent,
            logTag,
            "Android не смог открыть настройки установки APK.",
            out var error))
        {
            return true;
        }

        onError(error ?? "Android не смог открыть настройки установки APK.");
        return false;
    }

    public static bool TryResolveInstalledPackageSource(
        PackageManager? packageManager,
        string? packageName,
        out string? sourceDirectory,
        out string[] splitApks)
    {
        sourceDirectory = null;
        splitApks = [];
        if (packageManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            return false;
        }

        try
        {
            var app = packageManager.GetApplicationInfo(packageName, PackageInfoFlags.MatchDisabledComponents);
            if ((app.Flags & ApplicationInfoFlags.Installed) == 0 || string.IsNullOrWhiteSpace(app.SourceDir))
            {
                return false;
            }

            var parts = new List<string>();
            AddPart(parts, app.SourceDir);
            if (app.SplitSourceDirs is not null)
            {
                foreach (var splitSourceDir in app.SplitSourceDirs)
                {
                    AddPart(parts, splitSourceDir);
                }
            }

            if (!AreInstallPartsAvailable(parts))
            {
                return false;
            }

            sourceDirectory = app.SourceDir;
            splitApks = app.SplitSourceDirs?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? [];
            return true;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
            || AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    public static bool TryStartInstall(
        Activity activity,
        string? packageName,
        string? apkPath,
        string[]? splitApks,
        PendingIntent callbackPendingIntent,
        string logTag,
        Action<string> onError)
    {
        try
        {
            if (activity.PackageManager?.PackageInstaller is null)
            {
                onError("Android не смог подготовить пакет к установке.");
                return true;
            }

            var installParts = ResolveFreshInstallParts(activity.PackageManager, packageName, apkPath, splitApks, out var error);
            if (installParts is null)
            {
                onError(error ?? StaleApkMessage);
                return true;
            }

            StartSessionInstall(activity, installParts, callbackPendingIntent, logTag, onError);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            onError(StaleApkMessage);
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to start package install: {exception}");
            onError("Android не разрешил начать установку пакета.");
            return true;
        }
    }

    public static bool TryStartUninstall(Activity activity, string? packageName, PendingIntent callbackPendingIntent)
    {
        if (activity.PackageManager?.PackageInstaller is null || string.IsNullOrWhiteSpace(packageName))
            return false;

        try
        {
            activity.PackageManager.PackageInstaller.Uninstall(packageName, callbackPendingIntent.IntentSender);
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(nameof(AndroidPackageApi), $"Failed to start uninstall for {packageName}: {exception}");
            return false;
        }
    }

    private static void StartSessionInstall(
        Activity activity,
        IReadOnlyList<string> installParts,
        PendingIntent callbackPendingIntent,
        string logTag,
        Action<string> onError)
    {
        var packageInstaller = activity.PackageManager!.PackageInstaller;
        int sessionId;
        try
        {
            sessionId = packageInstaller.CreateSession(new PackageInstaller.SessionParams(PackageInstallMode.FullInstall));
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"Failed to create install session: {exception}");
            onError("Android не смог создать сессию установки APK.");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var session = packageInstaller.OpenSession(sessionId);

                var writtenParts = 0;
                foreach (var path in installParts)
                {
                    var file = new JavaFile(path);
                    if (!file.Exists() || !file.CanRead())
                    {
                        throw new StaleInstallSourceException(path);
                    }

                    var uri = AndroidUri.FromFile(file)
                        ?? throw new InvalidOperationException($"Failed to prepare APK URI: {path}");
                    using var input = activity.ContentResolver?.OpenInputStream(uri)
                        ?? throw new InvalidOperationException($"Failed to open APK: {uri}");

                    using var output = session.OpenWrite(Guid.NewGuid().ToString("N"), 0, -1);
                    input.CopyTo(output);
                    session.Fsync(output);
                    writtenParts++;
                }

                if (writtenParts == 0)
                {
                    throw new InvalidOperationException("Android did not provide any APKs for installation.");
                }

                session.Commit(callbackPendingIntent.IntentSender);
            }
            catch (Exception exception)
            {
                try
                {
                    packageInstaller.AbandonSession(sessionId);
                }
                catch (Exception abandonException)
                {
                    Log.Warn(logTag, $"Failed to abandon install session {sessionId}: {abandonException}");
                }

                if (IsStaleInstallSourceException(exception))
                {
                    Log.Warn(logTag, $"Package installation source became unavailable: {exception}");
                    activity.RunOnUiThread(() => onError(StaleApkMessage));
                    return;
                }

                Log.Error(logTag, $"Package installation failed: {exception}");
                activity.RunOnUiThread(() => onError("Android не смог прочитать APK для установки."));
            }
        });
    }

    private static IReadOnlyList<string>? ResolveFreshInstallParts(
        PackageManager packageManager,
        string? packageName,
        string? apkPath,
        string[]? splitApks,
        out string? error)
    {
        error = null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            try
            {
                var app = packageManager.GetApplicationInfo(packageName, PackageInfoFlags.MatchDisabledComponents);
                if ((app.Flags & ApplicationInfoFlags.Installed) == 0)
                {
                    error = StaleApkMessage;
                    return null;
                }

                AddPart(parts, app.SourceDir);
                if (app.SplitSourceDirs is not null)
                {
                    foreach (var splitSourceDir in app.SplitSourceDirs)
                    {
                        AddPart(parts, splitSourceDir);
                    }
                }
            }
            catch (PackageManager.NameNotFoundException) when (!string.IsNullOrWhiteSpace(apkPath))
            {
                AddPart(parts, apkPath);
                if (splitApks is not null)
                {
                    foreach (var splitApk in splitApks)
                    {
                        AddPart(parts, splitApk);
                    }
                }
            }
        }
        else
        {
            AddPart(parts, apkPath);
            if (splitApks is not null)
            {
                foreach (var splitApk in splitApks)
                {
                    AddPart(parts, splitApk);
                }
            }
        }

        if (parts.Count == 0)
        {
            error = "Android не смог определить APK для установки.";
            return null;
        }

        if (!AreInstallPartsAvailable(parts))
        {
            error = StaleApkMessage;
            return null;
        }

        return parts;
    }

    private static void AddPart(List<string> parts, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            parts.Add(path);
        }
    }

    private static bool AreInstallPartsAvailable(IReadOnlyList<string> parts)
    {
        foreach (var part in parts)
        {
            var file = new JavaFile(part);
            if (!file.Exists() || !file.CanRead())
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsStaleInstallSourceException(Exception exception) =>
        exception is StaleInstallSourceException
            or JavaFileNotFoundException
            or FileNotFoundException
            || exception is JavaIOException javaIOException
                && javaIOException.Message?.Contains("ENOENT", StringComparison.OrdinalIgnoreCase) == true
            || exception.InnerException is not null && IsStaleInstallSourceException(exception.InnerException);

    private sealed class StaleInstallSourceException(string path)
        : IOException($"Install source is unavailable: {path}")
    {
    }
}
