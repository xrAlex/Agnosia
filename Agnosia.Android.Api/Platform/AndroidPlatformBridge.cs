using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Dashboard;
using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Agnosia.Platform;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Platform;

public sealed class AndroidPlatformBridge : IPlatformBridge
{
    private const string LogTag = "AgnosiaPlatformBridge";
    private const string ManagedProfileSettingsAction = "android.settings.MANAGED_PROFILE_SETTINGS";
    private const int ProvisioningWarmupAttempts = 5;
    private const int ProvisioningWarmupDelayMilliseconds = 2000;

    private readonly AndroidActivityCommandGateway _commandRunner;
    private readonly AndroidDashboardReader _dashboardReader;
    private readonly AndroidPermissionCoordinator _permissionCoordinator;
    private readonly AndroidAppCommandCoordinator _appCommandCoordinator;
    private WeakReference<IAndroidActivityHost>? _activityHostReference;

    public static AndroidPlatformBridge Instance { get; } = new();

    private AndroidPlatformBridge()
    {
        _commandRunner = new AndroidActivityCommandGateway(GetActivityHost);
        _dashboardReader = new AndroidDashboardReader(_commandRunner);
        _permissionCoordinator = new AndroidPermissionCoordinator(_commandRunner, StartProvisioningAsync);
        _appCommandCoordinator = new AndroidAppCommandCoordinator(
            _commandRunner,
            _permissionCoordinator);
    }

    public void AttachActivity(IAndroidActivityHost activityHost)
    {
        AgnosiaRuntime.Initialize(activityHost.CurrentActivity);
        _activityHostReference = new WeakReference<IAndroidActivityHost>(activityHost);
    }

    public void DetachActivity()
    {
        _activityHostReference = null;
    }

    public Task<DashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken = default)
    {
        return _dashboardReader.LoadDashboardAsync(cancellationToken);
    }

    public Task<DashboardSnapshot> LoadDashboardProfileAsync(CancellationToken cancellationToken = default)
    {
        return _dashboardReader.LoadDashboardProfileAsync(cancellationToken);
    }

    public Task<DashboardAppInventorySnapshot> LoadAppInventoryAsync(
        DashboardSnapshot profileSnapshot,
        CancellationToken cancellationToken = default)
    {
        return _dashboardReader.LoadAppInventoryAsync(profileSnapshot, cancellationToken);
    }

    public Task<byte[]?> LoadAppIconAsync(
        AppSnapshot app,
        CancellationToken cancellationToken = default)
    {
        return _dashboardReader.LoadAppIconAsync(app, cancellationToken);
    }

    public Task<IReadOnlyDictionary<AppItemKey, byte[]?>> LoadAppIconsAsync(
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken = default)
    {
        return _dashboardReader.LoadAppIconsAsync(apps, cancellationToken);
    }

    public Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken = default)
    {
        return _dashboardReader.LoadRecentLogsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return _permissionCoordinator.LoadPermissionsAsync(cancellationToken);
    }

    public Task<OperationResult> RequestPermissionAsync(PermissionKind permission,
        CancellationToken cancellationToken = default)
    {
        return _permissionCoordinator.RequestPermissionAsync(permission, cancellationToken);
    }

    public Task<OperationResult> OpenAppDetailsSettingsAsync(CancellationToken cancellationToken = default)
    {
        var result = _permissionCoordinator.OpenAppDetailsSettings();
        return Task.FromResult(result);
    }

    public Task<OperationResult> RevokeRuntimePermissionsAsync(
        AppSnapshot app,
        CancellationToken cancellationToken = default)
    {
        if (app.RuntimePermissions is not { Count: > 0 } runtimePermissions)
            return Task.FromResult(OperationResult.Success("У приложения нет runtime-разрешений для отзыва."));

        if (app.Profile != ProfileKind.Work)
            return Task.FromResult(OperationResult.Failure(
                "Runtime-разрешения можно отзывать только у приложений рабочего профиля."));

        return AndroidProfileCommandGateway.RevokeRuntimePermissionsInWorkProfileAsync(
            _commandRunner,
            app.PackageName,
            runtimePermissions,
            cancellationToken);
    }

    public async Task<bool> LoadOnboardingCompletedAsync(CancellationToken cancellationToken = default)
    {
        _ = GetInitializedActivity();
        return await Task.FromResult(LocalStorageManager.Instance.GetBoolean(StorageKeys.OnboardingCompleted));
    }

    public Task<OperationResult> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        _ = GetInitializedActivity();
        LocalStorageManager.Instance.SetBoolean(StorageKeys.OnboardingCompleted, true);
        return Task.FromResult(OperationResult.Success("Первичная настройка завершена."));
    }

    public async Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        var host = GetActivityHost();
        var activity = host.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (AndroidSystemApi.GetDevicePolicyManager(activity) is not { } policyManager)
            return OperationResult.Failure("На этом устройстве недоступны API политики устройства.");

        if (!AndroidProvisioningApi.CanStartManagedProfileProvisioning(policyManager))
            return CreateProvisioningBlockedResult(activity);

        var authKey = PrepareProvisioningAuthentication();
        var intent = CreateManagedProfileProvisioningIntent(activity, host.AdminReceiverType, authKey);
        var result = await _commandRunner.StartExternalActivityForResultAsync(intent, cancellationToken);
        return await CompleteProvisioningAsync(activity, result, cancellationToken);
    }

    private static OperationResult CreateProvisioningBlockedResult(Activity activity)
    {
        var diagnostics = AndroidWorkProfileDiagnosticsReader.Read(activity);
        Log.Warn(LogTag, $"Managed profile provisioning blocked. {diagnostics.ToLogString()}.");

        if (diagnostics.ManagedProfileExists)
            return MarkProfileResetRequired(
                "Android не разрешает создать новый рабочий профиль, потому что в системе уже есть другой или остаточный рабочий профиль. " +
                "Если рабочий профиль виден в настройках Android, удалите его и повторите создание профиля Agnosia. " +
                "Если Android больше не показывает рабочий профиль, перезагрузите устройство и попробуйте снова.");

        return OperationResult.Failure(
            "Android сейчас не разрешает создать рабочий профиль. Проверьте ограничения устройства и повторите попытку.");
    }

    private static string PrepareProvisioningAuthentication()
    {
        var authKey = AuthenticationUtility.CreateAndStoreKey();
        AuthenticationUtility.Reset();
        AgnosiaUtilities.MarkWorkProfileSetupStarted();
        AuthenticationUtility.TryStoreProvisioningKey(authKey);
        return authKey;
    }

    private static Intent CreateManagedProfileProvisioningIntent(
        Activity activity,
        Type adminReceiverType,
        string authKey)
    {
        var intent = new Intent(DevicePolicyManager.ActionProvisionManagedProfile);
        AndroidProvisioningApi.ConfigureManagedProfileProvisioningIntent(
            intent,
            AgnosiaUtilities.GetAdminComponent(activity, adminReceiverType),
            authKey);
        return intent;
    }

    private async Task<OperationResult> CompleteProvisioningAsync(
        Activity activity,
        AndroidActivityResult result,
        CancellationToken cancellationToken)
    {
        if (result.ResultCode != Result.Ok)
        {
            if (AgnosiaUtilities.HasAssociatedProfile(activity))
                return MarkProfileResetRequired(
                    "Android создал рабочий профиль, но Agnosia не может подтвердить управление им. " +
                    "Удалите рабочий профиль в настройках Android, затем создайте его заново через Agnosia.");

            AgnosiaUtilities.ClearWorkProfileConfiguredState();
            return OperationResult.Failure("Создание рабочего профиля отменено или отклонено Android.");
        }

        if (await WaitForWorkProfileAvailabilityAsync(cancellationToken))
        {
            AgnosiaUtilities.MarkWorkProfileReady();
            return OperationResult.Success("Рабочий профиль подключен.");
        }

        if (AgnosiaUtilities.HasAssociatedProfile(activity))
            return MarkProfileResetRequired(
                "Рабочий профиль создан, но сейчас недоступен для Agnosia. " +
                "Удалите рабочий профиль в настройках Android, затем создайте его заново через Agnosia.");

        AgnosiaUtilities.ClearWorkProfileConfiguredState();
        return OperationResult.Failure("Android не создал рабочий профиль. Запустите создание заново через Agnosia.");
    }

    public async Task<OperationResult> OpenWorkProfileSettingsAsync(CancellationToken cancellationToken = default)
    {
        var activity = GetInitializedActivity();

        var intents = new[]
        {
            new Intent(ManagedProfileSettingsAction),
            new Intent(Settings.ActionSyncSettings),
            new Intent(Settings.ActionSettings)
        };

        foreach (var intent in intents)
        {
            var result = await TryOpenSettingsIntentAsync(activity, intent, cancellationToken);
            if (!WasCanceledWithError(result))
                return OperationResult.Success("Проверьте удаление рабочего профиля в настройках Android.");
        }

        return OperationResult.Failure("Android не смог открыть настройки устройства.");
    }

    public Task<OperationResult> CloneAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.CloneAsync(app, cancellationToken);
    }

    public Task<OperationResult> UninstallAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.UninstallAsync(app, cancellationToken);
    }

    public Task<OperationResult> SetFrozenAsync(AppSnapshot app, bool hidden,
        CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.SetFrozenAsync(app, hidden, cancellationToken);
    }

    public Task<OperationResult> ForceFreezeAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.ForceFreezeAsync(app, cancellationToken);
    }

    public Task<OperationResult> CreateShortcutAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.CreateShortcutAsync(app, cancellationToken);
    }

    public Task<OperationResult> LaunchAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.LaunchAsync(app, cancellationToken);
    }

    public Task<OperationResult> SetInteractionAccessAsync(AppSnapshot app, bool enabled,
        CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.SetInteractionAccessAsync(app, enabled, cancellationToken);
    }

    public Task<OperationResult> SaveSettingsAsync(AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        var activity = GetInitializedActivity();
        return AndroidSettingsStore.SaveAsync(activity, settings, cancellationToken);
    }

    public string GetDeviceInfoString()
    {
        try
        {
            var activity = GetActivityHost().CurrentActivity;
            var appVersion = activity.PackageManager?.GetPackageInfo(
                activity.PackageName!, PackageInfoFlags.MatchAll)?.VersionName ?? "?";

            var manufacturer = Build.Manufacturer ?? "?";
            var model = Build.Model ?? "?";
            var brand = Build.Brand ?? "?";
            var device = Build.Device ?? "?";
            var release = Build.VERSION.Release ?? "?";
            var sdk = (int)Build.VERSION.SdkInt;

            return $"Agnosia v{appVersion}\n"
                + $"Device: {manufacturer} {model} ({device})\n"
                + $"Brand: {brand}\n"
                + $"Android: {release} (API {sdk})";
        }
        catch
        {
            return "Agnosia (device info unavailable)";
        }
    }

    public void NotifyManagedProfileProvisioned(Context context, Intent? intent)
    {
        AgnosiaRuntime.Initialize(context);
        AgnosiaUtilities.MarkManagedProfileProvisioned(context, intent);
    }

    private static OperationResult MarkProfileResetRequired(string message)
    {
        AgnosiaUtilities.MarkWorkProfileResetRequired();
        return OperationResult.Failure(message);
    }

    private async Task<bool> WaitForWorkProfileAvailabilityAsync(
        CancellationToken cancellationToken,
        int attempts = ProvisioningWarmupAttempts,
        int delayMilliseconds = ProvisioningWarmupDelayMilliseconds)
    {
        var activity = GetActivityHost().CurrentActivity;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (AgnosiaUtilities.HasWorkProfileTarget(activity) &&
                await _commandRunner.CanReachWorkProfileAsync(cancellationToken)) return true;

            if (attempt < attempts - 1) await Task.Delay(delayMilliseconds, cancellationToken);
        }

        return false;
    }

    private Activity GetInitializedActivity()
    {
        var activity = GetActivityHost().CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return activity;
    }

    private static bool WasCanceledWithError(AndroidActivityResult result)
    {
        return result.ResultCode == Result.Canceled
               && !string.IsNullOrWhiteSpace(AndroidActivityResultApi.ExtractError(result));
    }

    private async Task<AndroidActivityResult> TryOpenSettingsIntentAsync(
        Activity activity,
        Intent intent,
        CancellationToken cancellationToken)
    {
        if (activity.PackageManager is not { } packageManager
            || intent.ResolveActivity(packageManager) is null)
            return AndroidActivityResultApi.CreateCanceledResult("Android не нашёл подходящий экран настроек.");

        return await _commandRunner.StartExternalActivityForResultAsync(intent, cancellationToken);
    }

    private bool TryGetActivityHost(out IAndroidActivityHost activityHost)
    {
        if (_activityHostReference?.TryGetTarget(out var target) == true)
        {
            activityHost = target;
            return true;
        }

        activityHost = null!;
        return false;
    }

    private IAndroidActivityHost GetActivityHost()
    {
        return TryGetActivityHost(out var activityHost)
            ? activityHost
            : throw new InvalidOperationException("Agnosia is not attached to an active Android activity.");
    }
}
