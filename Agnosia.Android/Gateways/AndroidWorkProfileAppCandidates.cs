using System.Text.Json;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Gateways;

internal static class AndroidWorkProfileAppCandidates
{
    private const string LogTag = "AgnosiaProfileCommand";
    private const int MaxCandidateJsonBytes = 128 * 1024;

    public static string[] Collect(Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (context.PackageName is { Length: > 0 } ownPackage) names.Add(ownPackage);

        var personalCount = 0;
        try
        {
            if (context.PackageManager is { } packageManager)
            {
                foreach (var app in packageManager.GetInstalledApplications(AndroidSystemApi.GetInstalledApplicationFlags()))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (app.PackageName is { Length: > 0 } name && names.Add(name)) personalCount++;
                }
            }
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception)
                                          || exception is Java.Lang.Exception)
        {
            Log.Warn(LogTag, $"Could not enumerate personal package candidates: {exception.GetType().Name}.");
        }

        var launcherCount = 0;
        var storedCount = 0;
        try
        {
            var userManager = AndroidSystemApi.GetUserManager(context);
            var serial = ServiceRegistry.GetRequiredService<LocalStorageManager>()
                .GetLong(StorageKeys.ManagedProfileUserSerial, -1);
            foreach (var name in AndroidKnownWorkPackages.Read(serial))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (names.Add(name)) storedCount++;
            }
            var launcherApps = context.GetSystemService(Context.LauncherAppsService) as LauncherApps;
            var profile = serial >= 0 ? userManager?.GetUserForSerialNumber(serial) : null;
            if (profile is not null && launcherApps?.Profiles?.Any(
                    visible => userManager!.GetSerialNumberForUser(visible) == serial) == true)
            {
                foreach (var activity in launcherApps.GetActivityList(null, profile) ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (activity.ApplicationInfo?.PackageName is { Length: > 0 } name && names.Add(name))
                        launcherCount++;
                }
            }
            else
                Log.Warn(LogTag,
                    $"LauncherApps work profile unavailable for candidate discovery. storedSerial={serial}.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception)
                                          || exception is Java.Lang.Exception)
        {
            Log.Warn(LogTag, $"Could not enumerate LauncherApps work candidates: {exception.GetType().Name}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (personalCount == 0 && launcherCount == 0 && storedCount == 0)
        {
            Log.Warn(LogTag, "Island-style work candidate discovery returned no packages from either source.");
            return [];
        }

        var candidates = names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(candidates).Length;
        if (payloadBytes > MaxCandidateJsonBytes)
        {
            Log.Warn(LogTag,
                $"Work app candidate list exceeds the command payload limit. packages={candidates.Length}, bytes={payloadBytes}, limit={MaxCandidateJsonBytes}.");
            return [];
        }

        Log.Info(LogTag,
            $"Island-style work candidate discovery completed. personal={personalCount}, launcher={launcherCount}, stored={storedCount}, distinct={candidates.Length}, bytes={payloadBytes}.");
        return candidates;
    }
}
