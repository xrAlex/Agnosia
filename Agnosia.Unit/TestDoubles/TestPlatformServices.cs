using Agnosia.Models;
using Agnosia.Platform;
using Agnosia.Services;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Avalonia.Threading;

namespace Agnosia.Unit.TestDoubles;

public sealed class TestPlatformServices :
    IDashboardPlatformService,
    IPlatformEventLogReader,
    IPermissionPlatformService,
    IOnboardingPlatformService,
    IAppCommandService,
    ISettingsPlatformService,
    IModulePlatformService
{
    public DashboardSnapshot DashboardProfile { get; set; } = DashboardSnapshot.Unsupported;

    public DashboardAppInventorySnapshot AppInventory { get; set; } = DashboardAppInventorySnapshot.Empty;

    public IReadOnlyList<AppLogEntry> RecentLogs { get; set; } = [];

    public IReadOnlyList<PermissionSnapshot> Permissions { get; set; } = [];

    public IReadOnlyList<AgnosiaModuleSnapshot> Modules { get; set; } =
        [AgnosiaModuleSnapshot.FileShuttleUnavailable];

    public bool OnboardingCompleted { get; set; } = true;

    public OperationResult DefaultOperationResult { get; set; } = OperationResult.Success("Ok");

    public Func<DashboardSnapshot, CancellationToken, Task<DashboardAppInventorySnapshot>>? LoadAppInventoryHandler
    {
        get;
        set;
    }

    public Func<IReadOnlyList<AppSnapshot>, CancellationToken, Task<IReadOnlyDictionary<AppItemKey, byte[]?>>>?
        LoadAppIconsHandler { get; set; }

    public Func<PermissionKind, CancellationToken, Task<OperationResult>>? RequestPermissionHandler { get; set; }

    public Func<CancellationToken, Task<OperationResult>>? CompleteOnboardingHandler { get; set; }

    public Func<AppSnapshot, CancellationToken, Task<OperationResult>>? CloneHandler { get; set; }

    public Func<AppSnapshot, CancellationToken, Task<OperationResult>>? UninstallHandler { get; set; }

    public Func<AppSnapshot, bool, CancellationToken, Task<OperationResult>>? SetFrozenHandler { get; set; }

    public Func<AppSnapshot, CancellationToken, Task<OperationResult>>? ForceFreezeHandler { get; set; }

    public Func<AppSnapshot, CancellationToken, Task<OperationResult>>? CreateShortcutHandler { get; set; }

    public Func<AppSnapshot, CancellationToken, Task<OperationResult>>? LaunchHandler { get; set; }

    public Func<AppSnapshot, CancellationToken, Task<OperationResult>>? RevokeRuntimePermissionsHandler { get; set; }

    public int DashboardProfileLoadCount { get; private set; }

    public int AppInventoryLoadCount { get; private set; }

    public int PermissionLoadCount { get; private set; }

    public int RequestPermissionCallCount { get; private set; }

    public int CompleteOnboardingCallCount { get; private set; }

    public int StartProvisioningCallCount { get; private set; }

    public int OpenWorkProfileSettingsCallCount { get; private set; }

    public List<AppSnapshot> CloneRequests { get; } = [];

    public List<AppSnapshot> UninstallRequests { get; } = [];

    public List<(AppSnapshot App, bool Hidden)> SetFrozenRequests { get; } = [];

    public List<AppSnapshot> ForceFreezeRequests { get; } = [];

    public List<AppSnapshot> CreateShortcutRequests { get; } = [];

    public List<AppSnapshot> LaunchRequests { get; } = [];

    public List<AppSnapshot> RevokeRuntimePermissionsRequests { get; } = [];

    public List<(AppSnapshot App, bool Enabled)> SetInteractionAccessRequests { get; } = [];

    public List<IReadOnlyList<AppSnapshot>> AppIconLoadRequests { get; } = [];

    public List<PermissionKind> PermissionRequests { get; } = [];

    public List<AppSettingsSnapshot> SavedSettings { get; } = [];

    public Func<AppSettingsSnapshot, CancellationToken, Task<OperationResult>>? SaveSettingsHandler { get; set; }
    public int OpenDocumentsUiRequests { get; private set; }
    public Func<CancellationToken, Task<OperationResult>>? OpenDocumentsUiHandler { get; set; }
    public int ModuleLoadCount { get; private set; }
    public List<(AgnosiaModuleKind Module, bool Enabled)> SetModuleEnabledRequests { get; } = [];
    public Func<AgnosiaModuleKind, bool, CancellationToken, Task<OperationResult>>? SetModuleEnabledHandler { get; set; }

    public Task<DashboardSnapshot> LoadDashboardProfileAsync(CancellationToken cancellationToken = default)
    {
        DashboardProfileLoadCount++;

        return Task.FromResult(DashboardProfile);
    }

    public Task<DashboardAppInventorySnapshot> LoadAppInventoryAsync(
        DashboardSnapshot profileSnapshot,
        CancellationToken cancellationToken = default)
    {
        AppInventoryLoadCount++;

        return LoadAppInventoryHandler is null
            ? Task.FromResult(AppInventory)
            : LoadAppInventoryHandler(profileSnapshot, cancellationToken);
    }

    public Task<IReadOnlyDictionary<AppItemKey, byte[]?>> LoadAppIconsAsync(
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken = default)
    {
        AppIconLoadRequests.Add(apps.ToArray());

        if (LoadAppIconsHandler is not null)
            return LoadAppIconsHandler(apps, cancellationToken);

        IReadOnlyDictionary<AppItemKey, byte[]?> result = apps.ToDictionary(
            AppItemKey.FromSnapshot,
            app => app.IconPng,
            EqualityComparer<AppItemKey>.Default);

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RecentLogs);
    }

    public string GetDeviceInfoString()
    {
        return "unit-test-device";
    }

    public Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync(CancellationToken cancellationToken = default)
    {
        PermissionLoadCount++;

        return Task.FromResult(Permissions);
    }

    public Task<OperationResult> RequestPermissionAsync(
        PermissionKind permission,
        CancellationToken cancellationToken = default)
    {
        RequestPermissionCallCount++;
        PermissionRequests.Add(permission);

        return RequestPermissionHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : RequestPermissionHandler(permission, cancellationToken);
    }

    public Task<OperationResult> OpenAppDetailsSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DefaultOperationResult);
    }

    public Task<bool> LoadOnboardingCompletedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OnboardingCompleted);
    }

    public Task<OperationResult> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        CompleteOnboardingCallCount++;

        return CompleteOnboardingHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : CompleteOnboardingHandler(cancellationToken);
    }

    public Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        StartProvisioningCallCount++;

        return Task.FromResult(DefaultOperationResult);
    }

    public Task<OperationResult> OpenWorkProfileSettingsAsync(CancellationToken cancellationToken = default)
    {
        OpenWorkProfileSettingsCallCount++;

        return Task.FromResult(DefaultOperationResult);
    }

    public Task<OperationResult> CloneAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        CloneRequests.Add(app);

        return CloneHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : CloneHandler(app, cancellationToken);
    }

    public Task<OperationResult> UninstallAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        UninstallRequests.Add(app);

        return UninstallHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : UninstallHandler(app, cancellationToken);
    }

    public Task<OperationResult> SetFrozenAsync(
        AppSnapshot app,
        bool hidden,
        CancellationToken cancellationToken = default)
    {
        SetFrozenRequests.Add((app, hidden));

        return SetFrozenHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : SetFrozenHandler(app, hidden, cancellationToken);
    }

    public Task<OperationResult> ForceFreezeAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        ForceFreezeRequests.Add(app);

        return ForceFreezeHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : ForceFreezeHandler(app, cancellationToken);
    }

    public Task<OperationResult> CreateShortcutAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        CreateShortcutRequests.Add(app);

        return CreateShortcutHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : CreateShortcutHandler(app, cancellationToken);
    }

    public Task<OperationResult> LaunchAsync(AppSnapshot app, CancellationToken cancellationToken = default)
    {
        LaunchRequests.Add(app);

        return LaunchHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : LaunchHandler(app, cancellationToken);
    }

    public Task<OperationResult> SetInteractionAccessAsync(
        AppSnapshot app,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        SetInteractionAccessRequests.Add((app, enabled));

        return Task.FromResult(DefaultOperationResult);
    }

    public Task<OperationResult> RevokeRuntimePermissionsAsync(
        AppSnapshot app,
        CancellationToken cancellationToken = default)
    {
        RevokeRuntimePermissionsRequests.Add(app);

        return RevokeRuntimePermissionsHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : RevokeRuntimePermissionsHandler(app, cancellationToken);
    }

    public Task<OperationResult> SaveSettingsAsync(
        AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        SavedSettings.Add(settings);

        return SaveSettingsHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : SaveSettingsHandler(settings, cancellationToken);
    }

    public Task<OperationResult> OpenDocumentsUiAsync(CancellationToken cancellationToken = default)
    {
        OpenDocumentsUiRequests++;

        return OpenDocumentsUiHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : OpenDocumentsUiHandler(cancellationToken);
    }

    public Task<IReadOnlyList<AgnosiaModuleSnapshot>> LoadModulesAsync(CancellationToken cancellationToken = default)
    {
        ModuleLoadCount++;

        return Task.FromResult(Modules);
    }

    public Task<OperationResult> SetModuleEnabledAsync(
        AgnosiaModuleKind module,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        SetModuleEnabledRequests.Add((module, enabled));

        return SetModuleEnabledHandler is null
            ? Task.FromResult(DefaultOperationResult)
            : SetModuleEnabledHandler(module, enabled, cancellationToken);
    }
}

internal static class TestWorkspaceFactory
{
    public static DashboardWorkspaceViewModel Create(
        TestPlatformServices? services = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        services ??= new TestPlatformServices();

        return new DashboardWorkspaceViewModel(
            services,
            services,
            services,
            services,
            services,
            services,
            services,
            new BoundedAppEventLogService(),
            InvokeImmediatelyAsync,
            delayAsync);
    }

    public static AppItemViewModel CreateApp(
        DashboardWorkspaceViewModel owner,
        ProfileKind profile = ProfileKind.Personal,
        string packageName = "com.example.app",
        string label = "Example",
        bool isSystem = false,
        bool isHidden = false,
        bool canLaunch = true,
        bool isInstalled = true,
        bool interactionAllowed = false,
        AppPermissionRiskLevel permissionRiskLevel = AppPermissionRiskLevel.Safe,
        IReadOnlyList<string>? riskyPermissions = null)
    {
        return CreateApp(
            owner,
            TestSnapshots.App(
                profile,
                packageName,
                label,
                isSystem,
                isHidden,
                canLaunch,
                isInstalled,
                interactionAllowed,
                permissionRiskLevel,
                riskyPermissions));
    }

    public static AppItemViewModel CreateApp(DashboardWorkspaceViewModel owner, AppSnapshot snapshot)
    {
        return new AppItemViewModel(owner, snapshot);
    }

    public static PermissionItemViewModel CreatePermission(
        DashboardWorkspaceViewModel owner,
        PermissionKind kind,
        bool isGranted = false,
        bool canRequest = true,
        string grantedLabel = "Granted",
        string requestLabel = "Request")
    {
        return new PermissionItemViewModel(
            owner,
            TestSnapshots.Permission(
                kind,
                isGranted,
                canRequest,
                grantedLabel,
                requestLabel));
    }

    public static PermissionItemViewModel CreatePermission(
        DashboardWorkspaceViewModel owner,
        PermissionSnapshot snapshot)
    {
        return new PermissionItemViewModel(owner, snapshot);
    }

    private static ValueTask InvokeImmediatelyAsync(Action action, DispatcherPriority _)
    {
        action();
        return ValueTask.CompletedTask;
    }
}
