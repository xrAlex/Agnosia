using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidUri = Android.Net.Uri;
using File = Java.IO.File;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

public static class AndroidPackageApi
{
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
            using var installSource = PrepareInstallSource(packageName, apkPath);

            if (installSource.Uri is null || activity.PackageManager?.PackageInstaller is null)
            {
                onError("Android не смог подготовить пакет к установке.");
                return true;
            }

            StartSessionInstall(activity, installSource.Uri, splitApks, callbackPendingIntent, logTag, onError);
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
        AndroidUri baseUri,
        string[]? splitApks,
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
                var allUris = new List<AndroidUri> { baseUri };
                if (splitApks is not null)
                {
                    allUris.AddRange(splitApks
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(path => AndroidUri.FromFile(new File(path)))
                        .OfType<AndroidUri>());
                }

                var writtenParts = 0;
                foreach (var uri in allUris)
                {
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

                Log.Error(logTag, $"Package installation failed: {exception}");
                activity.RunOnUiThread(() => onError("Android не смог прочитать APK для установки."));
            }
        });
    }

    private static InstallSourceScope PrepareInstallSource(string? packageName, string? apkPath)
    {
        AndroidUri? uri = null;
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            uri = AndroidUri.FromParts("package", packageName, null);
        }

        if (string.IsNullOrWhiteSpace(apkPath))
            return new InstallSourceScope(uri, previousPolicy: null);
        
        uri = AndroidUri.FromFile(new File(apkPath));
        var previousPolicy = StrictMode.GetVmPolicy();
        StrictMode.SetVmPolicy(new StrictMode.VmPolicy.Builder().Build());

        return new InstallSourceScope(uri, previousPolicy);
    }

    private sealed class InstallSourceScope(AndroidUri? uri, StrictMode.VmPolicy? previousPolicy) : IDisposable
    {
        public AndroidUri? Uri { get; } = uri;

        public void Dispose()
        {
            if (previousPolicy is not null)
            {
                StrictMode.SetVmPolicy(previousPolicy);
            }
        }
    }
}
