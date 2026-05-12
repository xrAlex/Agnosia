using Agnosia.Models;
using Android.Content.PM;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

internal sealed class AndroidDashboardReader(AndroidActivityCommandGateway commandRunner)
{
    private const string LogTag = "AgnosiaTransientVpn";
    private static readonly TimeSpan SetupStateTimeout = TimeSpan.FromMinutes(3);

    public async Task<DashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var packageManager = activity.PackageManager;
        var isSupported =
            packageManager?.HasSystemFeature(PackageManager.FeatureDeviceAdmin) == true &&
            packageManager.HasSystemFeature(PackageManager.FeatureManagedUsers);

        if (!isSupported)
        {
            return DashboardSnapshot.Unsupported;
        }

        var storage = LocalStorageManager.Instance;
        var settings = AndroidSettingsStore.LoadSnapshot(storage);
        var showAllApps = settings.ShowAllApps;
        var hasAssociatedProfile = AgnosiaUtilities.HasAssociatedProfile(activity);
        var hasWorkProfileTarget = AgnosiaUtilities.HasWorkProfileTarget(activity);
        var storedHasSetup = storage.GetBoolean(StorageKeys.HasSetup);
        var onboardingCompleted = storage.GetBoolean(StorageKeys.OnboardingCompleted);
        var isSettingUp = storage.GetBoolean(StorageKeys.IsSettingUp);
        if (isSettingUp && IsSetupStateStale(storage, hasAssociatedProfile, hasWorkProfileTarget))
        {
            Log.Warn(LogTag, "Provisioning state timed out without a reachable work profile. Clearing stale setup state.");
            AgnosiaUtilities.ClearWorkProfileConfiguredState();
            isSettingUp = false;
            storedHasSetup = false;
            onboardingCompleted = false;
        }

        var workProfileAvailable = hasWorkProfileTarget && await TryReachWorkProfileWithRetryAsync(cancellationToken);
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

        var recoveryKind = GetWorkProfileRecoveryKind(
            isSettingUp,
            workProfileAvailable,
            hasAssociatedProfile,
            hasWorkProfileTarget,
            storedHasSetup,
            onboardingCompleted);
        if (recoveryKind != WorkProfileRecoveryKind.None)
        {
            Log.Warn(LogTag, $"Work profile needs user recovery. kind={recoveryKind}.");
        }

        var hasSetup = storedHasSetup && (workProfileAvailable || recoveryKind != WorkProfileRecoveryKind.None);
        isSettingUp = storage.GetBoolean(StorageKeys.IsSettingUp) && !hasSetup;

        var workAppsQuery = AppQueryResult.Empty;
        if (workProfileAvailable)
        {
            workAppsQuery = await QueryAppsAsync(ProfileKind.Work, showAllApps, cancellationToken);
        }

        var personalAppsQuery = await QueryAppsAsync(ProfileKind.Personal, showAllApps, cancellationToken);
        var interactionPackages = workAppsQuery.InteractionPackages.ToHashSet(StringComparer.Ordinal);

        return new DashboardSnapshot(
            IsSupported: true,
            HasSetup: hasSetup,
            IsSettingUp: isSettingUp,
            WorkProfileAvailable: workProfileAvailable,
            WorkProfileRecovery: recoveryKind,
            PersonalApps: MapApps(personalAppsQuery.Apps, ProfileKind.Personal, interactionPackages),
            WorkApps: MapApps(workAppsQuery.Apps, ProfileKind.Work, interactionPackages),
            Settings: settings);
    }

    public async Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (!LocalStorageManager.Instance.GetBoolean(StorageKeys.LoggingEnabled, true))
        {
            return [];
        }

        var logs = AndroidAppLogArchive.Load(activity).ToList();
        if (AgnosiaUtilities.HasWorkProfileTarget(activity)
            && await commandRunner.CanReachWorkProfileAsync(cancellationToken))
        {
            logs.AddRange(await AndroidProfileCommandGateway.QueryWorkLogsAsync(commandRunner, cancellationToken));
        }

        if (logs.Count == 0)
        {
            return [];
        }

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
            cancellationToken);
        if (payload is null)
            return AppQueryResult.Empty;

        return new AppQueryResult(payload.Apps, payload.InteractionPackages);
    }

    private async Task<bool> TryReachWorkProfileWithRetryAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        const int delayMs = 500;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (await commandRunner.CanReachWorkProfileAsync(cancellationToken))
            {
                return true;
            }

            if (attempt < maxAttempts - 1)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        return false;
    }

    private static bool IsSetupStateStale(
        LocalStorageManager storage,
        bool hasAssociatedProfile,
        bool hasWorkProfileTarget)
    {
        var startedAtUnixSeconds = storage.GetLong(StorageKeys.SetupStartedAtUtc);
        if (startedAtUnixSeconds <= 0)
        {
            return !hasAssociatedProfile && !hasWorkProfileTarget;
        }

        var startedAt = DateTimeOffset.FromUnixTimeSeconds(startedAtUnixSeconds);
        return DateTimeOffset.UtcNow - startedAt >= SetupStateTimeout;
    }

    private static WorkProfileRecoveryKind GetWorkProfileRecoveryKind(
        bool isSettingUp,
        bool workProfileAvailable,
        bool hasAssociatedProfile,
        bool hasWorkProfileTarget,
        bool storedHasSetup,
        bool onboardingCompleted)
    {
        if (isSettingUp || workProfileAvailable)
        {
            return WorkProfileRecoveryKind.None;
        }

        if (hasAssociatedProfile && !hasWorkProfileTarget)
        {
            return WorkProfileRecoveryKind.NotManagedByAgnosia;
        }

        if (hasAssociatedProfile && hasWorkProfileTarget)
        {
            return WorkProfileRecoveryKind.NotManagedByAgnosia;
        }

        return storedHasSetup || onboardingCompleted || hasWorkProfileTarget
            ? WorkProfileRecoveryKind.Unavailable
            : WorkProfileRecoveryKind.None;
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
                app.IconPng);
        }

        return mappedApps;
    }

    private sealed record AppQueryResult(
        IReadOnlyList<AppServiceModel> Apps,
        IReadOnlyList<string> InteractionPackages)
    {
        public static AppQueryResult Empty { get; } = new([], []);
    }
}
