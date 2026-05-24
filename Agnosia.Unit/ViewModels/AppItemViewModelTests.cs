using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class AppItemViewModelTests
{
    // Проверяет доступные действия для обычного личного приложения.
    [Fact]
    public void Personal_non_system_app_exposes_move_and_uninstall_controls()
    {
        var app = CreateApp(TestSnapshots.App(ProfileKind.Personal, isSystem: false));

        Assert.Equal("Personal", app.ProfileLabel);
        Assert.True(app.CanClone);
        Assert.True(app.CanMoveToWork);
        Assert.True(app.CanUninstall);
        Assert.False(app.ShowWorkControls);
        Assert.False(app.HasStatusTag);
        Assert.True(app.IsPermissionRiskSafe);
        Assert.False(app.IsPermissionRiskDangerous);
        Assert.False(app.IsPermissionRiskCritical);
        Assert.True(app.ShowPermissionRiskIndicator);
        Assert.False(app.HasRiskyPermissions);
        Assert.Empty(app.RiskyPermissionsText);
        Assert.Equal("Разрешения: OK", app.PermissionRiskTooltip);
        Assert.Equal("Open", app.LaunchLabel);
        Assert.Equal("CopyToWork", app.CloneLabel);
        Assert.Equal("AllowInteraction", app.InteractionLabel);
    }

    [Fact]
    public void Permission_lists_are_available_without_risk_reason()
    {
        var app = CreateApp(TestSnapshots.App(
            ProfileKind.Personal,
            manifestPermissions:
            [
                "android.permission.VIBRATE",
                "android.permission.INTERNET"
            ]));

        Assert.True(app.IsPermissionRiskSafe);
        Assert.False(app.HasRiskyPermissions);
        Assert.True(app.HasPermissionDetails);
        Assert.Equal($"VIBRATE{Environment.NewLine}INTERNET", app.ManifestPermissionsText);
        Assert.False(app.HasRuntimePermissions);
    }

    [Fact]
    public void Revoke_runtime_permissions_is_available_only_for_work_apps_with_runtime_permissions()
    {
        var personal = CreateApp(TestSnapshots.App(
            ProfileKind.Personal,
            runtimePermissions: ["android.permission.CAMERA"]));
        var work = CreateApp(TestSnapshots.App(
            ProfileKind.Work,
            runtimePermissions: ["android.permission.CAMERA"]));
        var workSystem = CreateApp(TestSnapshots.App(
            ProfileKind.Work,
            isSystem: true,
            runtimePermissions: ["android.permission.CAMERA"]));

        Assert.False(personal.CanRevokeRuntimePermissions);
        Assert.True(work.CanRevokeRuntimePermissions);
        Assert.False(workSystem.CanRevokeRuntimePermissions);
    }

    // Проверяет, что приложение рабочего профиля не получает межпрофильный обмен по умолчанию.
    [Fact]
    public void Work_app_defaults_interaction_access_to_disabled()
    {
        var app = CreateApp(TestSnapshots.App(ProfileKind.Work));

        Assert.True(app.ShowWorkControls);
        Assert.False(app.InteractionAllowed);
        Assert.Equal("AllowInteraction", app.InteractionLabel);
    }

    // Проверяет состояние скрытого системного приложения в рабочем профиле.
    [Fact]
    public void Work_hidden_system_app_exposes_isolation_state_without_mutating_controls()
    {
        var app = CreateApp(TestSnapshots.App(
            ProfileKind.Work,
            isSystem: true,
            isHidden: true,
            canLaunch: false));

        Assert.Equal("Work", app.ProfileLabel);
        Assert.True(app.ShowWorkControls);
        Assert.True(app.CanFreeze);
        Assert.True(app.IsAgnosiaManaged);
        Assert.True(app.HasStatusTag);
        Assert.True(app.ShowSecondaryRow);
        Assert.False(app.CanClone);
        Assert.False(app.CanUninstall);
        Assert.False(app.ShowPermissionRiskIndicator);
        Assert.True(app.ShowLaunch);
        Assert.Equal("Isolated", app.StatusTagLabel);
        Assert.Equal("UnfreezeAndOpen", app.LaunchLabel);
        Assert.Equal("CopyToPersonal", app.CloneLabel);
    }

    // Проверяет отображаемое состояние рейтинга разрешений.
    [Fact]
    public void Permission_risk_properties_reflect_snapshot_rating()
    {
        var app = CreateApp(TestSnapshots.App(
            ProfileKind.Personal,
            permissionRiskLevel: AppPermissionRiskLevel.Critical,
            riskyPermissions:
            [
                "android.permission.CAMERA",
                "android.permission.RECORD_AUDIO"
            ]));

        Assert.Equal(AppPermissionRiskLevel.Critical, app.PermissionRiskLevel);
        Assert.False(app.IsPermissionRiskSafe);
        Assert.False(app.IsPermissionRiskDangerous);
        Assert.True(app.IsPermissionRiskCritical);
        Assert.True(app.HasRiskyPermissions);
        Assert.Equal("CAMERA, RECORD_AUDIO", app.RiskyPermissionsText);
        Assert.Equal("Имеет опасные разрешения", app.PermissionRiskTooltip);
        Assert.Equal("Имеет опасные разрешения", app.PermissionRiskSummaryText);
        Assert.Contains("может использовать камеру", app.PermissionRiskReasons);
        Assert.Contains("может использовать микрофон", app.PermissionRiskReasons);
    }

    [Fact]
    public void Permission_risk_details_are_composed_from_snapshot_evidence()
    {
        var app = CreateApp(TestSnapshots.App(
            ProfileKind.Personal,
            permissionRiskLevel: AppPermissionRiskLevel.Dangerous,
            riskyPermissions:
            [
                "android.permission.READ_SMS",
                "android.permission.INTERNET"
            ],
            matchedPermissionRiskRuleIds:
            [
                "SU-SMS-READ-01",
                "SU-SMS-READ-NET-01"
            ],
            permissionRiskScoreBreakdown: new AppPermissionRiskScoreBreakdown(
                DataSensitivityScore: 4,
                PersistenceScore: 0,
                ExfiltrationScore: 2,
                ControlSurfaceScore: 0,
                StealthScore: 0,
                LegitimacyPenalty: 0,
                ConfidenceScore: 1),
            manifestPermissions:
            [
                "android.permission.READ_SMS",
                "android.permission.INTERNET"
            ],
            runtimePermissions:
            [
                "android.permission.READ_SMS"
            ]));

        Assert.Equal("Повышенный риск по разрешениям", app.PermissionRiskSummaryText);
        Assert.Contains("может читать или отправлять SMS", app.PermissionRiskReasons);
        Assert.Contains("имеет канал для передачи данных наружу", app.PermissionRiskReasons);
        Assert.Contains("совпало несколько рискованных правил (2)", app.PermissionRiskReasons);
        Assert.True(app.HasPermissionDetails);
        Assert.True(app.HasManifestPermissions);
        Assert.True(app.HasRuntimePermissions);
        Assert.Equal($"READ_SMS{Environment.NewLine}INTERNET", app.ManifestPermissionsText);
        Assert.Equal("READ_SMS", app.RuntimePermissionsText);
    }

    [Fact]
    public void Permission_risk_reasons_cover_catalog_permission_groups()
    {
        var app = CreateApp(TestSnapshots.App(
            ProfileKind.Work,
            permissionRiskLevel: AppPermissionRiskLevel.Critical,
            riskyPermissions:
            [
                "android.permission.PACKAGE_USAGE_STATS",
                "android.permission.QUERY_ALL_PACKAGES",
                "android.permission.REQUEST_INSTALL_PACKAGES",
                "android.permission.RECEIVE_BOOT_COMPLETED",
                "android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS",
                "android.permission.ACCESS_LOCAL_NETWORK",
                "android.permission.BLUETOOTH_SCAN",
                "android.permission.RANGING",
                "android.permission.READ_ASSIST_STRUCTURE_SCREEN_CONTENT",
                "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION"
            ]));

        Assert.Contains("может видеть статистику использования приложений", app.PermissionRiskReasons);
        Assert.Contains("может видеть список установленных приложений", app.PermissionRiskReasons);
        Assert.Contains("может устанавливать APK из внешних источников", app.PermissionRiskReasons);
        Assert.Contains("может запускаться после перезагрузки устройства", app.PermissionRiskReasons);
        Assert.Contains("может обходить ограничения энергосбережения", app.PermissionRiskReasons);
        Assert.Contains("может обращаться к устройствам в локальной сети", app.PermissionRiskReasons);
        Assert.Contains("может использовать Bluetooth для поиска или обмена с устройствами рядом", app.PermissionRiskReasons);
        Assert.Contains("может оценивать расстояние до  соседних устройств", app.PermissionRiskReasons);
        Assert.Contains("может получать содержимое экрана через assistant API", app.PermissionRiskReasons);
        Assert.Contains("может записывать экран устройства", app.PermissionRiskReasons);
    }

    // Проверяет обновление вычисляемых свойств и PropertyChanged при новом snapshot.
    [Fact]
    public void ApplySnapshot_updates_derived_properties_and_raises_change_notifications()
    {
        var app = CreateApp(TestSnapshots.App(ProfileKind.Personal, label: "Alpha", interactionAllowed: true));
        var changedProperties = new List<string?>();
        app.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        app.ApplySnapshot(TestSnapshots.App(
            ProfileKind.Personal,
            label: "Beta",
            isSystem: true,
            canLaunch: false,
            isInstalled: false,
            interactionAllowed: false,
            permissionRiskLevel: AppPermissionRiskLevel.Dangerous,
            riskyPermissions: ["android.permission.INTERNET"]));

        Assert.Equal("Beta", app.Label);
        Assert.Equal("B", app.Monogram);
        Assert.True(app.CanClone);
        Assert.False(app.CanMoveToWork);
        Assert.False(app.CanUninstall);
        Assert.False(app.ShowLaunch);
        Assert.Equal("NotInstalled", app.StatusTagLabel);
        Assert.Equal("AllowInteraction", app.InteractionLabel);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, app.PermissionRiskLevel);
        Assert.True(app.IsPermissionRiskDangerous);
        Assert.True(app.HasRiskyPermissions);
        Assert.Equal("INTERNET", app.RiskyPermissionsText);
        Assert.Contains(nameof(AppItemViewModel.Label), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.Monogram), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.CanMoveToWork), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.CanUninstall), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.ShowPermissionRiskIndicator), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.ShowLaunch), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.StatusTagLabel), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.InteractionLabel), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.PermissionRiskLevel), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.IsPermissionRiskDangerous), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.PermissionRiskTooltip), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.PermissionRiskSummaryText), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.PermissionRiskReasons), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.HasPermissionRiskReasons), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.HasRiskyPermissions), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.RiskyPermissionsText), changedProperties);
    }

    // Проверяет запрет смены идентичности app item через ApplySnapshot.
    [Fact]
    public void ApplySnapshot_rejects_identity_changes()
    {
        var app = CreateApp(TestSnapshots.App(ProfileKind.Personal, packageName: "com.example.alpha"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            app.ApplySnapshot(TestSnapshots.App(ProfileKind.Personal, packageName: "com.example.beta")));

        Assert.Equal("App item identity cannot be changed.", exception.Message);
    }

    private static AppItemViewModel CreateApp(AppSnapshot snapshot)
    {
        return TestWorkspaceFactory.CreateApp(TestWorkspaceFactory.Create(), snapshot);
    }
}
