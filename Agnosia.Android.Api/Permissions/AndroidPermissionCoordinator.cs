using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Dashboard;
using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Api.Permissions;

internal sealed class AndroidPermissionCoordinator(
    AndroidActivityCommandGateway commandRunner,
    Func<CancellationToken, Task<OperationResult>> startProvisioningAsync)
{
    public async Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var profileDiagnostics = AndroidWorkProfileDiagnosticsReader.Read(activity);
        var hasWorkProfileTarget = profileDiagnostics.CommandTargetResolvable;
        var workProfileAvailable = hasWorkProfileTarget
                                   && profileDiagnostics.AvailableToCrossProfileApps
                                   && profileDiagnostics.QuietModeEnabled != true
                                   && await commandRunner.CanReachWorkProfileAsync(cancellationToken);
        var hasSetup = LocalStorageManager.Instance.GetBoolean(StorageKeys.HasSetup)
                       || hasWorkProfileTarget
                       || profileDiagnostics.ManagedProfileExists;
        var usageAccessGranted = workProfileAvailable
                                 && await AndroidProfileCommandGateway.QueryWorkUsageStatsAccessAsync(commandRunner,
                                     cancellationToken);
        var workPackageInstallGranted = workProfileAvailable
                                        && await AndroidProfileCommandGateway.QueryWorkPackageInstallAccessAsync(
                                            commandRunner, cancellationToken);

        return
        [
            new PermissionSnapshot(
                PermissionKind.WorkProfile,
                "Рабочий профиль",
                "Основной профиль",
                "Нужен для изоляции клонированных приложений, скрытия пакетов и управления политиками рабочего пространства",
                hasSetup && workProfileAvailable,
                !hasSetup || !workProfileAvailable,
                "Подключен",
                hasSetup ? "Проверить профиль" : "Создать профиль"),
            new PermissionSnapshot(
                PermissionKind.Notifications,
                "Уведомления",
                "Основной профиль",
                "Необходимо для отображения фоновой активности приложения",
                AndroidPermissionApi.HasNotificationPermission(activity),
                OperatingSystem.IsAndroidVersionAtLeast(33),
                "Получено",
                "Разрешить"),
            new PermissionSnapshot(
                PermissionKind.VpnControl,
                "Временное управление VPN",
                "Основной профиль",
                "Позволяет приложению управлять VPN-соединениями",
                AndroidPermissionApi.IsVpnPrepared(activity),
                true,
                "Получено",
                "Разрешить"),
            new PermissionSnapshot(
                PermissionKind.PackageInstall,
                "Установка APK",
                "Рабочий профиль",
                "Нужна для копирования пользовательских приложений в рабочий профиль через установщик APK",
                workPackageInstallGranted,
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
                usageAccessGranted,
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
                AndroidPermissionApi.HasOverlayPermission(activity),
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
            PermissionKind.WorkProfile => await startProvisioningAsync(cancellationToken),
            PermissionKind.UsageStats => await RequestUsageStatsAccessAsync(cancellationToken),
            PermissionKind.Notifications => AndroidPermissionApi.RequestNotificationPermission(activity),
            PermissionKind.VpnControl => await AndroidPermissionApi.RequestVpnControlAsync(commandRunner, cancellationToken),
            PermissionKind.PackageInstall => await RequestPackageInstallAccessAsync(cancellationToken),
            PermissionKind.Overlay => AndroidPermissionApi.OpenAppDetailsSettings(activity),
            _ => OperationResult.Failure("Неизвестное разрешение.")
        };
    }

    public async Task EnsureUsageStatsAccessRequestedAsync(CancellationToken cancellationToken)
    {
        var storage = LocalStorageManager.Instance;
        if (storage.GetBoolean(StorageKeys.UsageStatsAccessPrompted)) return;

        if (await AndroidProfileCommandGateway.QueryWorkUsageStatsAccessAsync(commandRunner, cancellationToken))
        {
            storage.SetBoolean(StorageKeys.UsageStatsAccessPrompted, true);
            return;
        }

        var requestResult = await RequestUsageStatsAccessAsync(cancellationToken);
        if (requestResult.Succeeded) storage.SetBoolean(StorageKeys.UsageStatsAccessPrompted, true);
    }

    private async Task<OperationResult> RequestUsageStatsAccessAsync(CancellationToken cancellationToken)
    {
        var result = await RunWorkProfilePermissionRequestAsync(
            AgnosiaActions.RequestUsageStatsAccess,
            cancellationToken,
            "Откройте Agnosia в списке и включите доступ к истории использования.");
        if (result.Succeeded) LocalStorageManager.Instance.SetBoolean(StorageKeys.UsageStatsAccessPrompted, true);

        return result;
    }

    private Task<OperationResult> RequestPackageInstallAccessAsync(CancellationToken cancellationToken)
    {
        return RunWorkProfilePermissionRequestAsync(
            AgnosiaActions.RequestPackageInstallAccess,
            cancellationToken,
            "Включите установку APK из Agnosia в рабочем профиле.");
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

    public OperationResult OpenAppDetailsSettings()
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return AndroidPermissionApi.OpenAppDetailsSettings(activity);
    }
}
