using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Dashboard;

internal sealed class AndroidDashboardReader(AndroidActivityCommandGateway commandRunner)
{
    private const string LogTag = "AgnosiaProfileDetection";
    private static readonly TimeSpan SetupStateTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ProvisionedSetupStateTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WorkProfileOwnerCacheTtl = TimeSpan.FromSeconds(5);
    private readonly Lock _workProfileOwnerCacheSync = new();
    private CachedWorkProfileOwnerCheck? _cachedWorkProfileOwnerCheck;

    public async Task<DashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken)
    {
        var profileSnapshot = await LoadDashboardProfileAsync(cancellationToken).ConfigureAwait(false);
        var inventory = await LoadAppInventoryAsync(profileSnapshot, cancellationToken).ConfigureAwait(false);
        return profileSnapshot with
        {
            PersonalApps = inventory.PersonalApps,
            WorkApps = inventory.WorkApps
        };
    }

    public async Task<DashboardSnapshot> LoadDashboardProfileAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var packageManager = activity.PackageManager;
        var isSupported =
            packageManager?.HasSystemFeature(PackageManager.FeatureDeviceAdmin) == true &&
            packageManager.HasSystemFeature(PackageManager.FeatureManagedUsers);

        if (!isSupported) return DashboardSnapshot.Unsupported;

        var storage = LocalStorageManager.Instance;
        var settings = AndroidSettingsStore.LoadSnapshot(storage);
        var profileDiagnostics = AndroidWorkProfileDiagnosticsReader.Read(activity);
        var hasAssociatedProfile = profileDiagnostics.ManagedProfileExists;
        var hasWorkProfileTarget = profileDiagnostics.CommandTargetResolvable;
        var storedHasSetup = storage.GetBoolean(StorageKeys.HasSetup);
        var onboardingCompleted = storage.GetBoolean(StorageKeys.OnboardingCompleted);
        var isSettingUp = storage.GetBoolean(StorageKeys.IsSettingUp);
        var hasManagedProfileProvisionedSignal = storage.GetLong(StorageKeys.ManagedProfileProvisionedAtUtc) > 0;
        if (isSettingUp && IsSetupStateStale(
                storage,
                hasAssociatedProfile,
                hasWorkProfileTarget,
                hasManagedProfileProvisionedSignal))
        {
            Log.Warn(LogTag,
                "Provisioning state timed out without a reachable work profile. Clearing stale setup state.");
            AgnosiaUtilities.ClearWorkProfileConfiguredState();
            isSettingUp = false;
            storedHasSetup = false;
            onboardingCompleted = false;
            hasManagedProfileProvisionedSignal = false;
        }

        var ownerCheck = await ReadWorkProfileOwnerCheckAsync(
                profileDiagnostics,
                cancellationToken)
            .ConfigureAwait(false);
        var workProfileAvailable = ownerCheck.Kind == WorkProfileOwnerCheckKind.AppIsProfileOwner;
        Log.Info(
            LogTag,
            $"Work profile diagnostics. {profileDiagnostics.ToLogString()}; ping={ownerCheck.Kind}; {ownerCheck.DiagnosticReason}.");
        if (workProfileAvailable)
        {
            AgnosiaUtilities.MarkWorkProfileReady();
            storedHasSetup = true;
            onboardingCompleted = true;
        }
        else if (!isSettingUp
                 && !hasAssociatedProfile
                 && !hasWorkProfileTarget
                 && (storedHasSetup || onboardingCompleted))
        {
            Log.Warn(LogTag, "Previously configured work profile is no longer present. Clearing setup state.");
            AgnosiaUtilities.ClearWorkProfileConfiguredState();
            storedHasSetup = false;
            onboardingCompleted = false;
        }

        var workProfileState = GetWorkProfileState(
            isSettingUp,
            workProfileAvailable,
            hasManagedProfileProvisionedSignal,
            profileDiagnostics,
            storedHasSetup,
            onboardingCompleted,
            ownerCheck);
        var recoveryKind = GetWorkProfileRecoveryKind(workProfileState);
        var diagnosticReason = BuildDiagnosticReason(
            workProfileState,
            hasManagedProfileProvisionedSignal,
            profileDiagnostics,
            storedHasSetup,
            onboardingCompleted,
            ownerCheck);
        if (recoveryKind != WorkProfileRecoveryKind.None)
            Log.Warn(
                LogTag,
                $"Work profile needs user attention. state={workProfileState}, recovery={recoveryKind}, reason={diagnosticReason}.");

        var hasSetup = storedHasSetup && (workProfileAvailable || recoveryKind != WorkProfileRecoveryKind.None);
        isSettingUp = storage.GetBoolean(StorageKeys.IsSettingUp) && !hasSetup;

        return new DashboardSnapshot(
            true,
            hasSetup,
            isSettingUp,
            workProfileAvailable,
            workProfileState,
            recoveryKind,
            diagnosticReason,
            [],
            [],
            settings);
    }

    public async Task<DashboardAppInventorySnapshot> LoadAppInventoryAsync(
        DashboardSnapshot profileSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!profileSnapshot.IsSupported || !profileSnapshot.HasSetup) return DashboardAppInventorySnapshot.Empty;

        var showAllApps = profileSnapshot.Settings.ShowAllApps;
        var personalAppsTask = QueryAppsAsync(ProfileKind.Personal, showAllApps, cancellationToken);
        var workAppsTask = profileSnapshot.WorkProfileAvailable
            ? QueryAppsAsync(ProfileKind.Work, showAllApps, cancellationToken)
            : Task.FromResult(AppQueryResult.Empty);

        await Task.WhenAll(personalAppsTask, workAppsTask).ConfigureAwait(false);

        var personalAppsQuery = await personalAppsTask.ConfigureAwait(false);
        var workAppsQuery = await workAppsTask.ConfigureAwait(false);
        var interactionPackages = workAppsQuery.InteractionPackages.ToHashSet(StringComparer.Ordinal);

        return new DashboardAppInventorySnapshot(
            MapApps(personalAppsQuery.Apps, ProfileKind.Personal, interactionPackages),
            MapApps(workAppsQuery.Apps, ProfileKind.Work, interactionPackages));
    }

    public Task<byte[]?> LoadAppIconAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        return AndroidProfileCommandGateway.LoadAppIconAsync(commandRunner, app, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, byte[]?>> LoadAppIconsAsync(
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken)
    {
        return AndroidProfileCommandGateway.LoadAppIconsAsync(commandRunner, apps, cancellationToken);
    }

    public async Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (!LocalStorageManager.Instance.GetBoolean(StorageKeys.LoggingEnabled, true)) return [];

        var logs = AndroidAppLogArchive.Load(activity).ToList();
        if (AgnosiaUtilities.HasWorkProfileTarget(activity)
            && (await TryCheckWorkProfileOwnerWithRetryAsync(cancellationToken).ConfigureAwait(false)).Kind
            == WorkProfileOwnerCheckKind.AppIsProfileOwner)
            logs.AddRange(await AndroidProfileCommandGateway.QueryWorkLogsAsync(commandRunner, cancellationToken));

        if (logs.Count == 0) return [];

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var mergedLogs = new List<AppLogEntry>(logs.Count);
        mergedLogs.AddRange(logs.OrderBy(log => log.Timestamp)
            .ThenBy(log => log.Id, StringComparer.Ordinal)
            .Where(entry => seenIds.Add(entry.Id)));

        return mergedLogs;
    }

    private async Task<AppQueryResult> QueryAppsAsync(
        ProfileKind profile,
        bool showAll,
        CancellationToken cancellationToken)
    {
        var payload = await AndroidProfileCommandGateway.QueryAppsAsync(
            commandRunner,
            profile,
            showAll,
            cancellationToken).ConfigureAwait(false);
        
        return payload is null ? AppQueryResult.Empty : new AppQueryResult(payload.Apps, payload.InteractionPackages);
    }

    private async Task<WorkProfileOwnerCheckResult> ReadWorkProfileOwnerCheckAsync(
        WorkProfileDiagnostics profileDiagnostics,
        CancellationToken cancellationToken)
    {
        return CanAttemptWorkProfileOwnerCheck(profileDiagnostics)
            ? await TryCheckWorkProfileOwnerWithRetryAsync(cancellationToken).ConfigureAwait(false)
            : new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.TargetUnavailable,
                "crossProfileTarget=missing");
    }

    private static bool CanAttemptWorkProfileOwnerCheck(WorkProfileDiagnostics profileDiagnostics)
    {
        return profileDiagnostics.CommandTargetResolvable
               && profileDiagnostics.AvailableToCrossProfileApps
               && profileDiagnostics.QuietModeEnabled != true;
    }

    private async Task<WorkProfileOwnerCheckResult> TryCheckWorkProfileOwnerWithRetryAsync(
        CancellationToken cancellationToken)
    {
        if (TryGetCachedWorkProfileOwnerCheck(out var cachedResult)) return cachedResult;

        const int maxAttempts = 5;
        const int delayMs = 500;

        var lastResult = new WorkProfileOwnerCheckResult(
            WorkProfileOwnerCheckKind.Unreachable,
            "profilePing=notAttempted");
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            lastResult = await AndroidProfileCommandGateway.CheckWorkProfileOwnerAsync(
                commandRunner,
                cancellationToken).ConfigureAwait(false);
            if (lastResult.Kind is WorkProfileOwnerCheckKind.AppIsProfileOwner
                or WorkProfileOwnerCheckKind.AppInstalledButNotOwner
                or WorkProfileOwnerCheckKind.AuthenticationKeyMissing
                or WorkProfileOwnerCheckKind.TargetUnavailable)
            {
                SetCachedWorkProfileOwnerCheck(lastResult);
                return lastResult;
            }

            if (attempt < maxAttempts - 1) await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        SetCachedWorkProfileOwnerCheck(lastResult);
        return lastResult;
    }

    private bool TryGetCachedWorkProfileOwnerCheck(out WorkProfileOwnerCheckResult result)
    {
        lock (_workProfileOwnerCacheSync)
        {
            if (_cachedWorkProfileOwnerCheck is { } cached
                && DateTimeOffset.UtcNow - cached.CachedAt <= WorkProfileOwnerCacheTtl)
            {
                result = cached.Result;
                return true;
            }
        }

        result = new WorkProfileOwnerCheckResult(
            WorkProfileOwnerCheckKind.Unreachable,
            "profilePing=cacheMiss");
        return false;
    }

    private void SetCachedWorkProfileOwnerCheck(WorkProfileOwnerCheckResult result)
    {
        lock (_workProfileOwnerCacheSync)
        {
            _cachedWorkProfileOwnerCheck = new CachedWorkProfileOwnerCheck(result, DateTimeOffset.UtcNow);
        }
    }

    private static bool IsSetupStateStale(
        LocalStorageManager storage,
        bool hasAssociatedProfile,
        bool hasWorkProfileTarget,
        bool hasManagedProfileProvisionedSignal)
    {
        var startedAtUnixSeconds = storage.GetLong(StorageKeys.SetupStartedAtUtc);
        if (hasManagedProfileProvisionedSignal
            && !IsSetupStartedBefore(ProvisionedSetupStateTimeout, startedAtUnixSeconds))
            return false;

        if (startedAtUnixSeconds <= 0) return !hasAssociatedProfile && !hasWorkProfileTarget;

        return IsSetupStartedBefore(SetupStateTimeout, startedAtUnixSeconds);
    }

    private static bool IsSetupStartedBefore(TimeSpan timeout, long startedAtUnixSeconds)
    {
        if (startedAtUnixSeconds <= 0) return false;

        var startedAt = DateTimeOffset.FromUnixTimeSeconds(startedAtUnixSeconds);
        return DateTimeOffset.UtcNow - startedAt >= timeout;
    }

    private static WorkProfileStateKind GetWorkProfileState(
        bool isSettingUp,
        bool workProfileAvailable,
        bool hasManagedProfileProvisionedSignal,
        WorkProfileDiagnostics profileDiagnostics,
        bool storedHasSetup,
        bool onboardingCompleted,
        WorkProfileOwnerCheckResult ownerCheck)
    {
        if (workProfileAvailable) return WorkProfileStateKind.AppIsProfileOwner;

        if (ownerCheck.Kind == WorkProfileOwnerCheckKind.AppInstalledButNotOwner)
            return WorkProfileStateKind.AppInstalledInWorkProfileButNotOwner;

        if (isSettingUp) return WorkProfileStateKind.ProvisioningInProgress;

        if (!HasAgnosiaWorkProfileSignal(
                hasManagedProfileProvisionedSignal,
                profileDiagnostics,
                storedHasSetup,
                onboardingCompleted))
        {
            return profileDiagnostics.ManagedProfileExists
                ? WorkProfileStateKind.ForeignProfileOwner
                : WorkProfileStateKind.NoWorkProfile;
        }

        if (profileDiagnostics.QuietModeEnabled == true) return WorkProfileStateKind.WorkProfileQuietMode;

        if (profileDiagnostics.ManagedProfileExists && !profileDiagnostics.AvailableToCrossProfileApps)
            return WorkProfileStateKind.WorkProfileUnavailable;

        if (profileDiagnostics.ManagedProfileExists && !profileDiagnostics.CommandTargetResolvable)
            return WorkProfileStateKind.WorkProfileCommandTargetUnavailable;

        if (profileDiagnostics.ManagedProfileExists && ownerCheck.Kind == WorkProfileOwnerCheckKind.Unreachable)
            return WorkProfileStateKind.WorkProfileCommandChannelUnavailable;

        if (hasManagedProfileProvisionedSignal) return WorkProfileStateKind.WorkProfileCreatedButAppNotReady;

        if (storedHasSetup || onboardingCompleted || profileDiagnostics.CommandTargetResolvable)
            return WorkProfileStateKind.ErrorUnknownWithDiagnostics;

        return WorkProfileStateKind.NoWorkProfile;
    }

    private static bool HasAgnosiaWorkProfileSignal(
        bool hasManagedProfileProvisionedSignal,
        WorkProfileDiagnostics profileDiagnostics,
        bool storedHasSetup,
        bool onboardingCompleted)
    {
        return storedHasSetup
               || onboardingCompleted
               || hasManagedProfileProvisionedSignal
               || profileDiagnostics.CommandTargetResolvable;
    }

    private static WorkProfileRecoveryKind GetWorkProfileRecoveryKind(WorkProfileStateKind state)
    {
        return state switch
        {
            WorkProfileStateKind.WorkProfileQuietMode =>
                WorkProfileRecoveryKind.WorkProfileQuietMode,
            WorkProfileStateKind.WorkProfileUnavailable =>
                WorkProfileRecoveryKind.WorkProfileUnavailable,
            WorkProfileStateKind.WorkProfileCommandTargetUnavailable =>
                WorkProfileRecoveryKind.WorkProfileCommandTargetUnavailable,
            WorkProfileStateKind.WorkProfileCommandChannelUnavailable =>
                WorkProfileRecoveryKind.WorkProfileCommandChannelUnavailable,
            WorkProfileStateKind.WorkProfileCreatedButAppNotReady =>
                WorkProfileRecoveryKind.WorkProfileCreatedButAppNotReady,
            WorkProfileStateKind.AppInstalledInWorkProfileButNotOwner =>
                WorkProfileRecoveryKind.AppInstalledInWorkProfileButNotOwner,
            WorkProfileStateKind.ForeignProfileOwner =>
                WorkProfileRecoveryKind.ForeignProfileOwner,
            WorkProfileStateKind.ErrorUnknownWithDiagnostics =>
                WorkProfileRecoveryKind.ErrorUnknownWithDiagnostics,
            _ => WorkProfileRecoveryKind.None
        };
    }

    private static string BuildDiagnosticReason(
        WorkProfileStateKind state,
        bool hasManagedProfileProvisionedSignal,
        WorkProfileDiagnostics profileDiagnostics,
        bool storedHasSetup,
        bool onboardingCompleted,
        WorkProfileOwnerCheckResult ownerCheck)
    {
        return $"state={state}; managedProfileProvisioned={hasManagedProfileProvisionedSignal}; " +
               $"{profileDiagnostics.ToLogString()}; " +
               $"storedSetup={storedHasSetup}; onboardingCompleted={onboardingCompleted}; " +
               $"ownerCheck={ownerCheck.Kind}; {ownerCheck.DiagnosticReason}";
    }

    private static AppSnapshot[] MapApps(
        IReadOnlyList<AppServiceModel> apps,
        ProfileKind profile,
        HashSet<string> interactionPackages)
    {
        var mappedApps = new AppSnapshot[apps.Count];
        for (var index = 0; index < apps.Count; index++)
        {
            var app = apps[index];
            mappedApps[index] = new AppSnapshot(
                app.PackageName,
                app.Label,
                app.SourceDirectory,
                app.SplitApks,
                profile,
                app.IsSystem,
                app.IsHidden,
                app.CanLaunch,
                app.IsInstalled,
                profile == ProfileKind.Work && interactionPackages.Contains(app.PackageName),
                app.IconPng,
                app.PermissionRiskLevel,
                app.RiskyPermissions);
        }

        return mappedApps;
    }

    private sealed record AppQueryResult(
        IReadOnlyList<AppServiceModel> Apps,
        IReadOnlyList<string> InteractionPackages)
    {
        public static AppQueryResult Empty { get; } = new([], []);
    }

    private sealed record CachedWorkProfileOwnerCheck(
        WorkProfileOwnerCheckResult Result,
        DateTimeOffset CachedAt);
}
