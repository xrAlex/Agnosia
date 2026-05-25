using Agnosia.Models;

namespace Agnosia.Platform;

public sealed class UnsupportedPlatformBridge : IPlatformBridge
{
    private const string AndroidOnlyMessage = "Agnosia работает только на Android.";
    private const string ProvisioningMessage = "Agnosia требует Android с поддержкой рабочего профиля.";

    private static readonly Task<DashboardSnapshot> UnsupportedDashboardTask =
        Task.FromResult(DashboardSnapshot.Unsupported);

    private static readonly Task<DashboardAppInventorySnapshot> EmptyInventoryTask =
        Task.FromResult(DashboardAppInventorySnapshot.Empty);

    private static readonly Task<IReadOnlyList<AppLogEntry>> EmptyLogsTask =
        Task.FromResult<IReadOnlyList<AppLogEntry>>([]);

    private static readonly Task<IReadOnlyList<PermissionSnapshot>> EmptyPermissionsTask =
        Task.FromResult<IReadOnlyList<PermissionSnapshot>>([]);

    private static readonly Task<OperationResult> AndroidOnlyFailureTask =
        Task.FromResult(OperationResult.Failure(AndroidOnlyMessage));

    private static readonly Task<OperationResult> ProvisioningFailureTask =
        Task.FromResult(OperationResult.Failure(ProvisioningMessage));

    public static UnsupportedPlatformBridge Instance { get; } = new();

    private UnsupportedPlatformBridge()
    {
    }

    public Task<DashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken = default)
    {
        return UnsupportedDashboardTask;
    }

    public Task<DashboardSnapshot> LoadDashboardProfileAsync(CancellationToken cancellationToken = default)
    {
        return UnsupportedDashboardTask;
    }

    public Task<DashboardAppInventorySnapshot> LoadAppInventoryAsync(
        DashboardSnapshot profileSnapshot,
        CancellationToken cancellationToken = default)
    {
        return EmptyInventoryTask;
    }

    public Task<byte[]?> LoadAppIconAsync(
        AppSnapshot app,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<byte[]?>(null);
    }

    public Task<IReadOnlyDictionary<AppItemKey, byte[]?>> LoadAppIconsAsync(
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken = default)
    {
        var icons = new Dictionary<AppItemKey, byte[]?>();
        foreach (var app in apps) icons[AppItemKey.FromSnapshot(app)] = null;

        return Task.FromResult<IReadOnlyDictionary<AppItemKey, byte[]?>>(icons);
    }

    public string GetDeviceInfoString()
    {
        return "Agnosia (unsupported platform)";
    }

    public Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken = default)
    {
        return EmptyLogsTask;
    }

    public Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return EmptyPermissionsTask;
    }

    public Task<OperationResult> RequestPermissionAsync(PermissionKind permission,
        CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> OpenAppDetailsSettingsAsync(CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<bool> LoadOnboardingCompletedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<OperationResult> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success("Первичная настройка завершена."));
    }

    public Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        return ProvisioningFailureTask;
    }

    public Task<OperationResult> OpenWorkProfileSettingsAsync(CancellationToken cancellationToken = default)
    {
        return ProvisioningFailureTask;
    }

    public Task<OperationResult> CloneAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> UninstallAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> SetFrozenAsync(AppSnapshot app, bool hidden,
        CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> ForceFreezeAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> CreateShortcutAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> LaunchAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> SetInteractionAccessAsync(AppSnapshot app, bool enabled,
        CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> RevokeRuntimePermissionsAsync(
        AppSnapshot app,
        CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }

    public Task<OperationResult> SaveSettingsAsync(AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        return AndroidOnlyFailureTask;
    }
}
