using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.App;
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

        var localState = await ReadPermissionLocalStateAsync(activity, cancellationToken).ConfigureAwait(false);
        var profileDiagnostics = localState.ProfileDiagnostics;
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
            new PermissionSnapshot(
                PermissionKind.WorkProfile,
                "Рабочий профиль",
                "Основной профиль",
                "Нужен для изоляции клонированных приложений, скрытия пакетов и управления политиками рабочего пространства",
                localState.HasSetup && workProfileAvailable,
                !localState.HasSetup || !workProfileAvailable,
                "Подключен",
                localState.HasSetup ? "Проверить профиль" : "Создать профиль"),
            new PermissionSnapshot(
                PermissionKind.Notifications,
                "Уведомления",
                "Основной профиль",
                "Необходимо для отображения фоновой активности приложения",
                localState.NotificationPermissionGranted,
                OperatingSystem.IsAndroidVersionAtLeast(33),
                "Получено",
                "Разрешить"),
            new PermissionSnapshot(
                PermissionKind.VpnControl,
                "Временное управление VPN",
                "Основной профиль",
                "Позволяет приложению управлять VPN-соединениями",
                localState.VpnControlPrepared,
                true,
                "Получено",
                "Разрешить"),
            new PermissionSnapshot(
                PermissionKind.PackageInstall,
                "Установка APK",
                "Рабочий профиль",
                "Нужна для копирования пользовательских приложений в рабочий профиль через установщик APK",
                workPermissions.PackageInstallAccess,
                workProfileAvailable,
                "Получено",
                "Открыть настройки"),
            new PermissionSnapshot(
                PermissionKind.PersonalAllFiles,
                "Доступ к файлам",
                "Основной профиль",
                "Нужно для File Shuttle, чтобы Agnosia могла отдавать выбранные файлы личного профиля через DocumentsUI",
                localState.PersonalAllFilesGranted,
                true,
                "Получено",
                "Открыть настройки"),
            new PermissionSnapshot(
                PermissionKind.WorkAllFiles,
                "Доступ к файлам",
                "Рабочий профиль",
                "Нужно для File Shuttle, чтобы Agnosia могла отдавать выбранные файлы рабочего профиля через DocumentsUI",
                workPermissions.AllFilesAccess,
                workProfileAvailable,
                "Получено",
                "Открыть настройки"),
            new PermissionSnapshot(
                PermissionKind.UsageStats,
                "Доступ к истории использования",
                "Рабочий профиль",
                """
                Позволяет Agnosia понять, когда вы перестали использовать приложение, и заморозить его

                1. Нажмите Разрешить
                2. Пролистайте вниз
                3. Активируйте 'Доступ к истории использования'

                Если на этом шаге Android не выдал разрешение, тогда:

                4. Вернитесь назад
                5. В верхней правой части экрана нажмите на ⋮
                6. Выберите 'Разрешить доступ к настройкам'
                7. Пролистайте вниз
                8. Активируйте 'Доступ к истории использования'
                """,
                workPermissions.UsageStatsAccess,
                workProfileAvailable,
                "Получено",
                "Открыть настройки"),
            new PermissionSnapshot(
                PermissionKind.Overlay,
                "Поверх окон",
                "Основной профиль",
                """
                Необходимо для показа overlay-окна, которое позволяет запускать VPN после заморозки приложения в рабочем профиле.

                1. Нажмите Разрешить
                2. Пролистайте вниз
                3. Активируйте 'Поверх других приложений'
                
                Если на этом шаге Android не выдал разрешение, тогда:
                
                4. Вернитесь назад
                5. В верхней правой части экрана нажмите на ⋮
                6. Выберите 'Разрешить доступ к настройкам'
                7. Пролистайте вниз
                8. Активируйте 'Поверх других приложений'
                """,
                localState.OverlayPermissionGranted,
                true,
                "Получено",
                "Открыть настройки")
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
            PermissionKind.UsageStats => await RequestUsageStatsAccessAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.Notifications => AndroidPermissionApi.RequestNotificationPermission(activity),
            PermissionKind.VpnControl => await RequestVpnControlAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.PackageInstall => await RequestPackageInstallAccessAsync(cancellationToken)
                .ConfigureAwait(false),
            PermissionKind.PersonalAllFiles => AndroidPermissionApi.OpenAllFilesAccessSettings(activity),
            PermissionKind.WorkAllFiles => await RequestAllFilesAccessAsync(cancellationToken).ConfigureAwait(false),
            PermissionKind.Overlay => AndroidPermissionApi.OpenAppDetailsSettings(activity),
            _ => OperationResult.Failure("Неизвестное разрешение.")
        };
    }

    public async Task EnsureUsageStatsAccessRequestedAsync(CancellationToken cancellationToken)
    {
        var storage = LocalStorageManager.Instance;
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

    private async Task<OperationResult> RequestUsageStatsAccessAsync(CancellationToken cancellationToken)
    {
        var result = await RunWorkProfilePermissionRequestAsync(
                AgnosiaActions.RequestUsageStatsAccess,
                cancellationToken,
                "Откройте Agnosia в списке и включите доступ к истории использования.")
            .ConfigureAwait(false);
        if (result.Succeeded) LocalStorageManager.Instance.SetBoolean(StorageKeys.UsageStatsAccessPrompted, true);

        return result;
    }

    private async Task<OperationResult> RequestVpnControlAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        var storage = LocalStorageManager.Instance;
        Intent? prepareIntent;
        try
        {
            prepareIntent = VpnService.Prepare(activity);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn("AgnosiaPermissions", $"Failed to prepare VPN permission request: {exception}");
            storage.SetBoolean(StorageKeys.VpnControlPrepared, false);
            return OperationResult.Failure("Android не смог открыть запрос доступа к VPN.");
        }

        if (prepareIntent is null)
        {
            storage.SetBoolean(StorageKeys.VpnControlPrepared, true);
            return OperationResult.Success("VPN-доступ уже подготовлен.");
        }

        var result = await commandRunner.StartExternalActivityForResultAsync(prepareIntent, cancellationToken)
            .ConfigureAwait(false);
        var error = AndroidActivityResultApi.ExtractError(result);
        if (!string.IsNullOrWhiteSpace(error))
        {
            storage.SetBoolean(StorageKeys.VpnControlPrepared, false);
            return OperationResult.Failure(error);
        }

        var prepared = result.ResultCode == Result.Ok;
        storage.SetBoolean(StorageKeys.VpnControlPrepared, prepared);

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
            var hasWorkProfileTarget = profileDiagnostics.CommandTargetResolvable;
            var hasSetup = LocalStorageManager.Instance.GetBoolean(StorageKeys.HasSetup)
                           || hasWorkProfileTarget
                           || profileDiagnostics.ManagedProfileExists;
            return new PermissionLocalState(
                profileDiagnostics,
                hasSetup,
                AndroidPermissionApi.HasNotificationPermission(activity),
                LocalStorageManager.Instance.GetBoolean(StorageKeys.VpnControlPrepared),
                AndroidPermissionApi.HasAllFilesAccess(activity),
                AndroidPermissionApi.HasOverlayPermission(activity));
        }, cancellationToken);
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
        bool NotificationPermissionGranted,
        bool VpnControlPrepared,
        bool PersonalAllFilesGranted,
        bool OverlayPermissionGranted);
}
