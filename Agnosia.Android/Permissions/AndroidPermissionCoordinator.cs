using Agnosia.Models;
using Android.Content;
using Android.Net;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Permissions;

internal sealed class AndroidPermissionCoordinator(
    AndroidActivityCommandGateway commandRunner,
    Func<CancellationToken, Task<OperationResult>> startProvisioningAsync)
{
    public async Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var (profileDiagnostics, hasSetup, crossProfileInteractionGranted, crossProfileInteractionCanRequest, notificationPermissionGranted, vpnControlGranted, personalAllFilesGranted, overlayPermissionGranted) = await ReadPermissionLocalStateAsync(activity, cancellationToken).ConfigureAwait(false);
        var hasWorkProfileTarget = profileDiagnostics.CommandTargetResolvable;
        var workProfileAvailable = hasWorkProfileTarget
                                   && profileDiagnostics.AvailableToCrossProfileApps
                                   && profileDiagnostics.QuietModeEnabled != true
                                   && await commandRunner.CanReachWorkProfileAsync(cancellationToken)
                                       .ConfigureAwait(false);
        var workPermissions = workProfileAvailable
            ? await AndroidProfileCommandGateway.QueryWorkPermissionsAsync(commandRunner, cancellationToken)
                .ConfigureAwait(false)
            : WorkProfilePermissionQueryResult.Empty;

        return
        [
            PermissionCatalog.CreateWorkProfileSnapshot(hasSetup, workProfileAvailable),
            PermissionCatalog.CreateCrossProfileInteractionSnapshot(
                crossProfileInteractionGranted,
                crossProfileInteractionCanRequest),
            PermissionCatalog.CreateSnapshot(
                PermissionKind.Notifications,
                notificationPermissionGranted,
                OperatingSystem.IsAndroidVersionAtLeast(33)),
            PermissionCatalog.CreateSnapshot(
                PermissionKind.VpnControl,
                vpnControlGranted,
                true),
            PermissionCatalog.CreateSnapshot(
                PermissionKind.PackageInstall,
                workPermissions.PackageInstallAccess,
                workProfileAvailable),
            PermissionCatalog.CreateSnapshot(
                PermissionKind.PersonalAllFiles,
                personalAllFilesGranted,
                true),
            PermissionCatalog.CreateSnapshot(
                PermissionKind.WorkAllFiles,
                workPermissions.AllFilesAccess,
                workProfileAvailable),
            PermissionCatalog.CreateSnapshot(
                PermissionKind.UsageStats,
                workPermissions.UsageStatsAccess,
                workProfileAvailable),
            PermissionCatalog.CreateSnapshot(
                PermissionKind.Overlay,
                overlayPermissionGranted,
                true)
        ];
    }

    public async Task<OperationResult> RequestPermissionAsync(
        PermissionKind permission,
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        return permission switch
        {
            PermissionKind.WorkProfile => await startProvisioningAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.CrossProfileInteraction => await RequestCrossProfileInteractionAsync(cancellationToken)
                .ConfigureAwait(false),
            PermissionKind.UsageStats => await RequestUsageStatsAccessAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.Notifications => AndroidPermissionApi.RequestNotificationPermission(activity),
            PermissionKind.VpnControl => await RequestVpnControlAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.PackageInstall => await RequestPackageInstallAccessAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.PersonalAllFiles => AndroidPermissionApi.OpenAllFilesAccessSettings(activity),
            PermissionKind.WorkAllFiles => await RequestAllFilesAccessAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.Overlay => AndroidPermissionApi.OpenOverlaySettings(activity),
            _ => OperationResult.Failure("Неизвестное разрешение.")
        };
    }

    public async Task EnsureUsageStatsAccessRequestedAsync(CancellationToken cancellationToken)
    {
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        if (storage.GetBoolean(StorageKeys.UsageStatsAccessPrompted)) return;

        if (await AndroidProfileCommandGateway.QueryWorkUsageStatsAccessAsync(commandRunner, cancellationToken)
                .ConfigureAwait(false))
        {
            storage.SetBoolean(StorageKeys.UsageStatsAccessPrompted, true);
            return;
        }

        var requestResult = await RequestUsageStatsAccessAsync(cancellationToken).ConfigureAwait(false);
        if (requestResult.Succeeded) storage.SetBoolean(StorageKeys.UsageStatsAccessPrompted, true);
    }

    private async Task<OperationResult> RequestCrossProfileInteractionAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(activity);
        if (crossProfileApps is null)
            return OperationResult.Failure("Android не предоставил API межпрофильного взаимодействия.");

        if (crossProfileApps.CanInteractAcrossProfiles())
            return OperationResult.Success("Межпрофильное взаимодействие уже включено.");

        if (!crossProfileApps.CanRequestInteractAcrossProfiles())
            return OperationResult.Failure(
                "Android не даёт открыть системный запрос межпрофильного доступа для Agnosia как владельца рабочего профиля. Для отладочного устройства выдайте INTERACT_ACROSS_PROFILES через adb appops.");

        var result = await commandRunner.StartExternalActivityForResultAsync(
                crossProfileApps.CreateRequestInteractAcrossProfilesIntent(),
                cancellationToken)
            .ConfigureAwait(false);
        var error = AndroidActivityResultApi.ExtractError(result);
        if (!string.IsNullOrWhiteSpace(error))
            return OperationResult.Failure(error);

        return crossProfileApps.CanInteractAcrossProfiles()
            ? OperationResult.Success("Межпрофильное взаимодействие включено.")
            : OperationResult.Success("Включите межпрофильное взаимодействие для Agnosia на системном экране.");
    }

    private async Task<OperationResult> RequestUsageStatsAccessAsync(CancellationToken cancellationToken)
    {
        var result = await RunWorkProfilePermissionRequestAsync(
                AgnosiaActions.RequestUsageStatsAccess,
                cancellationToken,
                "Откройте Agnosia в списке и включите доступ к истории использования.")
            .ConfigureAwait(false);
        if (result.Succeeded) ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.UsageStatsAccessPrompted, true);

        return result;
    }

    private async Task<OperationResult> RequestVpnControlAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        Intent? prepareIntent;
        try
        {
            prepareIntent = VpnService.Prepare(activity);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn("AgnosiaPermissions", $"Failed to prepare VPN permission request: {exception}");
            return OperationResult.Failure("Android не смог открыть запрос доступа к VPN.");
        }

        if (prepareIntent is null)
        {
            return OperationResult.Success("VPN-доступ уже подготовлен.");
        }

        var result = await commandRunner.StartExternalActivityForResultAsync(prepareIntent, cancellationToken)
            .ConfigureAwait(false);
        var error = AndroidActivityResultApi.ExtractError(result);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return OperationResult.Failure(error);
        }

        var prepared = result.ResultCode == Result.Ok;

        return prepared
            ? OperationResult.Success("VPN-доступ подготовлен.")
            : OperationResult.Failure("Android не выдал доступ к VPN.");
    }

    private Task<OperationResult> RequestPackageInstallAccessAsync(CancellationToken cancellationToken)
    {
        return RunWorkProfilePermissionRequestAsync(
            AgnosiaActions.RequestPackageInstallAccess,
            cancellationToken,
            "Включите установку APK из Agnosia в рабочем профиле.");
    }

    private Task<OperationResult> RequestAllFilesAccessAsync(CancellationToken cancellationToken)
    {
        return RunWorkProfilePermissionRequestAsync(
            AgnosiaActions.RequestAllFilesAccess,
            cancellationToken,
            "Включите доступ ко всем файлам для Agnosia в рабочем профиле.");
    }

    private Task<OperationResult> RunWorkProfilePermissionRequestAsync(
        string action,
        CancellationToken cancellationToken,
        string successMessage)
    {
        return commandRunner.RunVoidOperationAsync(
            new Intent(action),
            true,
            cancellationToken,
            successMessage);
    }

    private static Task<PermissionLocalState> ReadPermissionLocalStateAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profileDiagnostics = AndroidWorkProfileDiagnosticsReader.Read(activity);
            var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(activity);
            var hasWorkProfileTarget = profileDiagnostics.CommandTargetResolvable;
            var hasSetup = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.HasSetup)
                           || hasWorkProfileTarget
                           || profileDiagnostics.ManagedProfileExists;
            return new PermissionLocalState(
                profileDiagnostics,
                hasSetup,
                crossProfileApps?.CanInteractAcrossProfiles() == true,
                crossProfileApps?.CanRequestInteractAcrossProfiles() == true,
                AndroidPermissionApi.HasNotificationPermission(activity),
                IsVpnControlGranted(activity),
                AndroidPermissionApi.HasAllFilesAccess(activity),
                AndroidPermissionApi.HasOverlayPermission(activity));
        }, cancellationToken);
    }

    private static bool IsVpnControlGranted(Activity activity)
    {
        try
        {
            return VpnService.Prepare(activity) is null;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn("AgnosiaPermissions", $"Failed to read VPN permission state: {exception}");
            return false;
        }
    }

    public OperationResult OpenAppDetailsSettings()
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return AndroidPermissionApi.OpenAppDetailsSettings(activity);
    }

    private sealed record PermissionLocalState(
        WorkProfileDiagnostics ProfileDiagnostics,
        bool HasSetup,
        bool CrossProfileInteractionGranted,
        bool CrossProfileInteractionCanRequest,
        bool NotificationPermissionGranted,
        bool VpnControlGranted,
        bool PersonalAllFilesGranted,
        bool OverlayPermissionGranted);
}
