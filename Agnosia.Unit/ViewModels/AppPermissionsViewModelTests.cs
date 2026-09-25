using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class AppPermissionsViewModelTests
{
    [Fact]
    public async Task Denies_only_selected_permission_and_reloads_actual_state()
    {
        var services = new TestPlatformServices();
        var camera = Permission("vendor.permission.CAMERA", AppPermissionState.NotGranted);
        var internet = Permission("android.permission.INTERNET", AppPermissionState.Granted) with
        { Kind = AppPermissionKind.Manifest, CanChangePolicy = false };
        services.AppPermissions = [camera, internet];
        services.SetAppPermissionHandler = (_, name, denied, _) =>
        {
            Assert.Equal(camera.Name, name);
            Assert.True(denied);
            services.AppPermissions = [camera with { State = AppPermissionState.PolicyDenied }, internet];
            return Task.FromResult(OperationResult.Success("Запрещено"));
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.ChangePolicyCommand.ExecuteAsync(vm.Items[0]);

        Assert.Equal("Запрещено", vm.Items[0].StatusText);
        Assert.Equal("Снять запрет", vm.Items[0].ActionText);
        Assert.False(vm.Items[1].CanChangePolicy);
        Assert.Equal("Выдано · нельзя отозвать", vm.Items[1].StatusText);
    }

    [Fact]
    public async Task Removing_policy_does_not_pretend_to_grant_permission()
    {
        var permission = Permission("custom.permission.ACCESS", AppPermissionState.PolicyDenied);
        var services = new TestPlatformServices { AppPermissions = [permission] };
        services.SetAppPermissionHandler = (_, _, denied, _) =>
        {
            Assert.False(denied);
            services.AppPermissions = [permission with { State = AppPermissionState.NotGranted }];
            return Task.FromResult(OperationResult.Success("Запрет снят"));
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.ChangePolicyCommand.ExecuteAsync(vm.Items[0]);
        Assert.Equal("Не выдано", vm.Items[0].StatusText);
    }

    [Fact]
    public async Task Failed_refresh_removes_stale_actions_and_preserves_error()
    {
        var services = new TestPlatformServices
        { AppPermissions = [Permission("custom.permission.ACCESS", AppPermissionState.Granted)] };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);
        services.LoadAppPermissionsHandler = (_, _) => throw new InvalidOperationException("Профиль недоступен");
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(vm.Items);
        Assert.True(vm.HasError);
        Assert.Contains("Профиль недоступен", vm.Message);
    }

    [Fact]
    public async Task Personal_profile_cannot_change_policy_even_if_payload_claims_it_can()
    {
        var services = new TestPlatformServices
        { AppPermissions = [Permission("custom.permission.ACCESS", AppPermissionState.Granted)] };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Personal));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.Items[0].CanChangePolicy);
        Assert.False(vm.ChangePolicyCommand.CanExecute(vm.Items[0]));
    }

    private static AppPermissionSnapshot Permission(string name, AppPermissionState state) =>
        new(name, name, null, AppPermissionKind.Runtime, state, true, null,
            CanRevokeGrant: state is AppPermissionState.Granted or AppPermissionState.PolicyGranted);

    [Fact]
    public async Task Search_matches_dictionary_label_and_full_permission_name()
    {
        var services = new TestPlatformServices
        {
            AppPermissions =
            [
                Permission("android.permission.ACCESS_COARSE_LOCATION", AppPermissionState.NotGranted),
                Permission("android.permission.INTERNET", AppPermissionState.Granted)
            ]
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SearchText = "место";
        Assert.Equal("android.permission.ACCESS_COARSE_LOCATION", Assert.Single(vm.VisibleItems).Name);
        vm.SearchText = "internet";
        Assert.Equal("android.permission.INTERNET", Assert.Single(vm.VisibleItems).Name);
        vm.SearchText = "absent";
        Assert.True(vm.HasNoSearchResults);
    }

    [Fact]
    public async Task Failed_mutation_keeps_android_state_and_error()
    {
        var services = new TestPlatformServices
        {
            AppPermissions = [Permission("fixed.permission", AppPermissionState.Granted)],
            SetAppPermissionHandler = (_, _, _, _) => Task.FromResult(OperationResult.Failure("Закреплено системой"))
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.ChangePolicyCommand.ExecuteAsync(vm.Items[0]);
        Assert.True(vm.HasError);
        Assert.Equal("Закреплено системой", vm.Message);
        Assert.Equal("Выдано", vm.Items[0].StatusText);
    }

    [Fact]
    public async Task Refresh_disables_mutations_and_old_rows_cannot_execute_after_reload()
    {
        var services = new TestPlatformServices
        { AppPermissions = [Permission("custom.permission.ACCESS", AppPermissionState.Granted)] };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);
        var oldRow = vm.Items[0];
        var pending = new TaskCompletionSource<IReadOnlyList<AppPermissionSnapshot>>();
        services.LoadAppPermissionsHandler = (_, _) => pending.Task;
        var refresh = vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.ChangePolicyCommand.CanExecute(oldRow));
        Assert.False(vm.RevokeCommand.CanExecute(oldRow));
        pending.SetResult(services.AppPermissions);
        await refresh;
        Assert.False(vm.ChangePolicyCommand.CanExecute(oldRow));
        Assert.False(vm.RevokeCommand.CanExecute(oldRow));
        Assert.True(vm.ChangePolicyCommand.CanExecute(vm.Items[0]));
    }

    [Fact]
    public async Task Revoking_a_granted_permission_leaves_it_requestable()
    {
        var camera = Permission("android.permission.CAMERA", AppPermissionState.Granted);
        var services = new TestPlatformServices { AppPermissions = [camera] };
        services.SetAppPermissionHandler = (_, _, _, _) => throw new Exception("Не менять постоянную политику");
        services.RevokeAppPermissionHandler = (_, name, _) =>
        {
            Assert.Equal(camera.Name, name);
            services.AppPermissions = [camera with { State = AppPermissionState.NotGranted, CanRevokeGrant = false }];
            return Task.FromResult(OperationResult.Success("Доступ отозван"));
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.RevokeCommand.CanExecute(vm.Items[0]));
        await vm.RevokeCommand.ExecuteAsync(vm.Items[0]);

        Assert.Equal("Не выдано", vm.Items[0].StatusText);
        Assert.False(vm.Items[0].CanRevoke);
        Assert.True(vm.Items[0].CanChangePolicy);
        Assert.Equal("Доступ отозван", vm.Message);
    }

    [Fact]
    public async Task Revoke_is_only_available_for_granted_runtime_permissions_in_work_profile()
    {
        var granted = Permission("android.permission.CAMERA", AppPermissionState.Granted);
        var notGranted = Permission("android.permission.RECORD_AUDIO", AppPermissionState.NotGranted);
        var blocked = Permission("android.permission.READ_CONTACTS", AppPermissionState.PolicyDenied);
        var policyGranted = Permission("android.permission.READ_SMS", AppPermissionState.PolicyGranted);
        var manifest = Permission("android.permission.INTERNET", AppPermissionState.Granted) with
        { Kind = AppPermissionKind.Manifest, CanChangePolicy = false, CanRevokeGrant = false };
        var services = new TestPlatformServices { AppPermissions = [granted, notGranted, blocked, policyGranted, manifest] };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.Items.Single(row => row.Name == granted.Name).CanRevoke);
        Assert.False(vm.Items.Single(row => row.Name == notGranted.Name).CanRevoke);
        Assert.False(vm.Items.Single(row => row.Name == blocked.Name).CanRevoke);
        Assert.True(vm.Items.Single(row => row.Name == policyGranted.Name).CanRevoke);
        Assert.False(vm.Items.Single(row => row.Name == manifest.Name).CanRevoke);

        var personal = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Personal));
        await personal.RefreshCommand.ExecuteAsync(null);
        Assert.False(personal.Items.Single(row => row.Name == granted.Name).CanRevoke);
    }

    [Fact]
    public async Task Failed_revoke_shows_error_and_reloads_actual_grant()
    {
        var granted = Permission("android.permission.CAMERA", AppPermissionState.Granted);
        var services = new TestPlatformServices
        {
            AppPermissions = [granted],
            RevokeAppPermissionHandler = (_, _, _) =>
                Task.FromResult(OperationResult.Failure("Android отказал в отзыве"))
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.RevokeCommand.ExecuteAsync(vm.Items[0]);

        Assert.True(vm.HasError);
        Assert.Equal("Android отказал в отзыве", vm.Message);
        Assert.Equal("Выдано", vm.Items[0].StatusText);
        Assert.True(vm.Items[0].CanRevoke);
    }

    [Fact]
    public async Task Sorts_dangerous_permissions_first_and_granted_before_ungranted_within_each_kind()
    {
        var services = new TestPlatformServices
        {
            AppPermissions =
            [
                Permission("android.permission.INTERNET", AppPermissionState.Granted) with { Kind = AppPermissionKind.Manifest },
                Permission("unknown.permission", AppPermissionState.Unknown) with { Kind = AppPermissionKind.Unknown },
                Permission("android.permission.CAMERA", AppPermissionState.NotGranted),
                Permission("android.permission.SYSTEM_ALERT_WINDOW", AppPermissionState.Granted) with { Kind = AppPermissionKind.Special },
                Permission("android.permission.RECORD_AUDIO", AppPermissionState.Granted)
            ]
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(
            ["android.permission.RECORD_AUDIO", "android.permission.CAMERA",
             "android.permission.SYSTEM_ALERT_WINDOW", "android.permission.INTERNET", "unknown.permission"],
            vm.Items.Select(row => row.Name));
    }

    [Theory]
    [InlineData("android.permission.CAMERA", "Камера")]
    [InlineData("android.permission.INTERNET", "Интернет")]
    [InlineData("android.permission.ACCESS_BACKGROUND_LOCATION", "Геолокация в фоне")]
    [InlineData("android.permission.BIND_VPN_SERVICE", "Подключение VPN приложения")]
    [InlineData("com.android.voicemail.permission.READ_VOICEMAIL", "Чтение голосовой почты")]
    [InlineData("com.android.alarm.permission.SET_ALARM", "Установка будильника")]
    [InlineData("com.android.launcher.permission.INSTALL_SHORTCUT", "Ярлыки")]
    [InlineData("android.permission.ACCESS_LOCAL_NETWORK", "Локальная сеть")]
    [InlineData("android.permission.health.READ_STEPS", "Шаги — чтение")]
    [InlineData("android.permission.health.WRITE_HEART_RATE", "Пульс — запись")]
    [InlineData("android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND", "Данные о здоровье при закрытом приложении")]
    [InlineData("android.permission.health.READ_MEDICAL_DATA_VACCINES", "Прививки — чтение")]
    [InlineData("android.permission.ACCESS_ADSERVICES_AD_ID", "Рекламный идентификатор Android")]
    [InlineData("com.google.android.gms.permission.AD_ID", "Рекламный идентификатор Google Play")]
    [InlineData("com.android.vending.BILLING", "Покупки через Google Play")]
    [InlineData("com.android.vending.CHECK_LICENSE", "Проверка покупки приложения")]
    [InlineData("android.permission.INSTALL_SHORTCUT", "Старое добавление ярлыков")]
    [InlineData("com.google.android.c2dm.permission.RECEIVE", "Старые push-сообщения Google")]
    public void Dictionary_provides_plain_language_names_and_descriptions(string name, string expectedLabel)
    {
        var text = Assert.IsType<AppPermissionText>(AppPermissionDictionary.Find(name));
        Assert.Equal(expectedLabel, text.Label);
        Assert.False(string.IsNullOrWhiteSpace(text.Description));
    }

    [Fact]
    public void Dictionary_includes_Health_Connect_permissions_from_Android_9_onward()
    {
        Assert.True(AppPermissionDictionary.Count >= 513);
        Assert.NotNull(AppPermissionDictionary.Find("android.permission.health.READ_BLOOD_PRESSURE"));
        Assert.NotNull(AppPermissionDictionary.Find("android.permission.health.WRITE_BLOOD_PRESSURE"));
        Assert.Null(AppPermissionDictionary.Find("vendor.permission.PRIVATE"));
    }

    [Fact]
    public async Task Unknown_permission_ignores_android_label_and_has_no_guessed_description()
    {
        var services = new TestPlatformServices
        {
            AppPermissions = [Permission("vendor.permission.PRIVATE", AppPermissionState.Unknown) with
            { Label = "Private access", Description = "Some vendor text" }]
        };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Items);
        Assert.Equal("Неизвестное разрешение", row.Label);
        Assert.False(row.HasDescription);
        Assert.Equal("vendor.permission.PRIVATE", row.Name);
    }

    [Fact]
    public async Task Granted_non_revocable_permission_marks_status_next_to_granted()
    {
        var manifest = Permission("android.permission.INTERNET", AppPermissionState.Granted) with
        { Kind = AppPermissionKind.Manifest, CanChangePolicy = false };
        var runtime = Permission("android.permission.CAMERA", AppPermissionState.Granted);
        var legacyRuntime = Permission("android.permission.RECORD_AUDIO", AppPermissionState.Granted) with
        { CanRevokeGrant = false };
        var services = new TestPlatformServices { AppPermissions = [manifest, runtime, legacyRuntime] };
        var vm = new AppPermissionsViewModel(services, TestSnapshots.App(ProfileKind.Work));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Выдано · нельзя отозвать", vm.Items.Single(row => row.Name == manifest.Name).StatusText);
        Assert.Equal("Выдано", vm.Items.Single(row => row.Name == runtime.Name).StatusText);
        Assert.Equal("Выдано · нельзя отозвать", vm.Items.Single(row => row.Name == legacyRuntime.Name).StatusText);
        Assert.True(vm.Items.Single(row => row.Name == legacyRuntime.Name).CanChangePolicy);
    }
}
