using Agnosia.Models;
using Agnosia.Platform;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Agnosia.Android.Platform;

public sealed class AndroidPlatformBridge : IPlatformBridge
{
    private readonly AndroidActivityCommandGateway _commandRunner;
    private readonly AndroidDashboardReader _dashboardReader;
    private readonly AndroidPermissionCoordinator _permissionCoordinator;
    private readonly AndroidAppCommandCoordinator _appCommandCoordinator;
    private readonly AndroidModuleCoordinator _moduleCoordinator;
    private readonly AndroidProvisioningCoordinator _provisioningCoordinator;
    private readonly AndroidSettingsCoordinator _settingsCoordinator;
    private WeakReference<IAndroidActivityHost>? _activityHostReference;

    public static AndroidPlatformBridge Instance { get; } = new();

    private AndroidPlatformBridge()
    {
        _commandRunner = new AndroidActivityCommandGateway(GetActivityHost);
        _provisioningCoordinator = new AndroidProvisioningCoordinator(_commandRunner, GetActivityHost);
        _settingsCoordinator = new AndroidSettingsCoordinator(GetInitializedActivity);
        _dashboardReader = new AndroidDashboardReader(_commandRunner);
        _permissionCoordinator = new AndroidPermissionCoordinator(
            _commandRunner,
            _provisioningCoordinator.StartProvisioningAsync);
        _appCommandCoordinator = new AndroidAppCommandCoordinator(
            _commandRunner,
            _permissionCoordinator);
        _moduleCoordinator = new AndroidModuleCoordinator(
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

    public Task<bool> LoadOnboardingCompletedAsync(CancellationToken cancellationToken = default)
    {
        return _settingsCoordinator.LoadOnboardingCompletedAsync(cancellationToken);
    }

    public Task<OperationResult> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        return _settingsCoordinator.CompleteOnboardingAsync(cancellationToken);
    }

    public Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        return _provisioningCoordinator.StartProvisioningAsync(cancellationToken);
    }

    public async Task<OperationResult> OpenWorkProfileSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _provisioningCoordinator.OpenWorkProfileSettingsAsync(cancellationToken).ConfigureAwait(false);
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

    public Task<OperationResult> SetLockdownInternetAccessAsync(AppSnapshot app, bool blocked,
        CancellationToken cancellationToken = default)
    {
        return _appCommandCoordinator.SetLockdownInternetAccessAsync(app, blocked, cancellationToken);
    }

    public Task<OperationResult> SaveSettingsAsync(AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        return _settingsCoordinator.SaveSettingsAsync(settings, cancellationToken);
    }

    public Task<OperationResult> OpenDocumentsUiAsync(CancellationToken cancellationToken = default)
    {
        return _settingsCoordinator.OpenDocumentsUiAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AgnosiaModuleSnapshot>> LoadModulesAsync(CancellationToken cancellationToken = default)
    {
        return _moduleCoordinator.LoadModulesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AgnosiaModuleSnapshot>> LoadModulesAsync(
        IReadOnlyList<PermissionSnapshot> permissions,
        CancellationToken cancellationToken = default)
    {
        return _moduleCoordinator.LoadModulesAsync(permissions, cancellationToken);
    }

    public Task<OperationResult> SetModuleEnabledAsync(
        AgnosiaModuleKind module,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return _moduleCoordinator.SetModuleEnabledAsync(module, enabled, cancellationToken);
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
        _provisioningCoordinator.NotifyManagedProfileProvisioned(context, intent);
    }

    private Activity GetInitializedActivity()
    {
        var activity = GetActivityHost().CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return activity;
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
