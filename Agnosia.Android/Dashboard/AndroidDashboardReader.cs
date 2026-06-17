using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.App;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Dashboard;

internal sealed class AndroidDashboardReader(AndroidActivityCommandGateway commandRunner)
{
    private const string LogTag = "AgnosiaProfileDetection";

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

        var localState = await ReadDashboardProfileLocalStateAsync(activity, cancellationToken).ConfigureAwait(false);
        if (!localState.IsSupported) return DashboardSnapshot.Unsupported;

        var settings = localState.Settings ?? AppSettingsSnapshot.Default;
        var hadConfiguredWorkProfile = localState.HadConfiguredWorkProfile;
        var profileDiagnostics = localState.ProfileDiagnostics
                                 ?? throw new InvalidOperationException("Profile diagnostics were not loaded.");
        var ownerCheck = await ReadWorkProfileOwnerCheckAsync(
                profileDiagnostics,
                cancellationToken)
            .ConfigureAwait(false);
        var statusMessage = string.Empty;
        if (hadConfiguredWorkProfile && ShouldUpdateWorkProfileApp(localState.LocalVersionCode, ownerCheck))
        {
            Log.Info(
                LogTag,
                $"Work profile Agnosia version mismatch; starting update. ownerCheck={ownerCheck.DiagnosticReason}; workVersionCode={ownerCheck.AppVersionCode}; localVersionCode={localState.LocalVersionCode}.");
            statusMessage = "UpdatingWorkProfile";
            AndroidWorkProfileOwnerCheckCache.Clear();
            var updateResult = await AndroidProfileCommandGateway.UpdateAgnosiaInWorkProfileAsync(
                    commandRunner,
                    cancellationToken)
                .ConfigureAwait(false);
            AndroidWorkProfileOwnerCheckCache.Clear();
            ownerCheck = updateResult.Succeeded
                ? await ReadWorkProfileOwnerCheckAsync(profileDiagnostics, cancellationToken).ConfigureAwait(false)
                : new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.VersionUpdateFailed,
                    $"profileUpdate=failed; message={updateResult.Message}; previous={ownerCheck.DiagnosticReason}",
                    ownerCheck.AppVersionCode,
                    ownerCheck.AppVersionName);

            if (updateResult.Succeeded && ShouldUpdateWorkProfileApp(localState.LocalVersionCode, ownerCheck))
                ownerCheck = new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.VersionUpdateFailed,
                    $"profileUpdate=stillMismatch; previous={ownerCheck.DiagnosticReason}",
                    ownerCheck.AppVersionCode,
                    ownerCheck.AppVersionName);

            statusMessage = ownerCheck.Kind == WorkProfileOwnerCheckKind.VersionUpdateFailed
                ? "WorkProfileUpdateFailed"
                : "WorkProfileUpdated";
        }

        var workProfileState = ResolveWorkProfileState(
            hadConfiguredWorkProfile,
            profileDiagnostics,
            ownerCheck);
        SynchronizeStorageWithResolvedState(workProfileState);

        var workProfileAvailable = workProfileState == WorkProfileStateKind.Available;
        var hasSetup = workProfileState == WorkProfileStateKind.Available
                       || workProfileState == WorkProfileStateKind.Unavailable;
        var recoveryKind = ResolveWorkProfileRecoveryKind(workProfileState, profileDiagnostics, ownerCheck);
        var diagnosticReason = BuildDiagnosticReason(workProfileState, profileDiagnostics, ownerCheck);

        Log.Info(
            LogTag,
            $"Work profile check. state={workProfileState}; {profileDiagnostics.ToLogString()}; " +
            $"ownerCheck={ownerCheck.Kind}; {ownerCheck.DiagnosticReason}.");
        if (recoveryKind != WorkProfileRecoveryKind.None)
            Log.Warn(LogTag, $"Work profile must be recreated. reason={diagnosticReason}.");

        return new DashboardSnapshot(
            true,
            hasSetup,
            false,
            workProfileAvailable,
            workProfileState,
            recoveryKind,
            diagnosticReason,
            [],
            [],
            settings,
            statusMessage);
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

    public Task<IReadOnlyDictionary<AppItemKey, byte[]?>> LoadAppIconsAsync(
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken)
    {
        return AndroidProfileCommandGateway.LoadAppIconsAsync(commandRunner, apps, cancellationToken);
    }

    public async Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var localState = await ReadRecentLogLocalStateAsync(activity, cancellationToken).ConfigureAwait(false);
        if (!localState.LoggingEnabled) return [];

        var logs = localState.Logs.ToList();
        var profileDiagnostics = localState.ProfileDiagnostics;
        if (CanAttemptWorkProfileOwnerCheck(profileDiagnostics)
            && (await AndroidProfileCommandGateway.CheckWorkProfileOwnerAsync(commandRunner, cancellationToken)
                .ConfigureAwait(false)).Kind == WorkProfileOwnerCheckKind.AppIsProfileOwner)
            logs.AddRange(await AndroidProfileCommandGateway.QueryWorkLogsAsync(commandRunner, cancellationToken)
                .ConfigureAwait(false));

        if (logs.Count == 0) return [];

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var mergedLogs = new List<AppLogEntry>(logs.Count);
        mergedLogs.AddRange(logs.OrderBy(log => log.Timestamp)
            .ThenBy(log => log.Id, StringComparer.Ordinal)
            .Where(entry => seenIds.Add(entry.Id)));

        return mergedLogs;
    }

    private static Task<DashboardProfileLocalState> ReadDashboardProfileLocalStateAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packageManager = activity.PackageManager;
            var isSupported =
                packageManager?.HasSystemFeature(PackageManager.FeatureDeviceAdmin) == true &&
                packageManager.HasSystemFeature(PackageManager.FeatureManagedUsers);

            if (!isSupported) return new DashboardProfileLocalState(false, null, false, null, 0);

            var storage = LocalStorageManager.Instance;
            return new DashboardProfileLocalState(
                true,
                AndroidSettingsStore.LoadSnapshot(storage),
                HasConfiguredWorkProfileSignal(storage),
                AndroidWorkProfileDiagnosticsReader.Read(activity),
                GetLocalVersionCode(packageManager, activity.PackageName));
        }, cancellationToken);
    }

    private static Task<RecentLogLocalState> ReadRecentLogLocalStateAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var loggingEnabled = LocalStorageManager.Instance.GetBoolean(StorageKeys.LoggingEnabled, true);
            if (!loggingEnabled)
                return new RecentLogLocalState(false, [], CreateEmptyWorkProfileDiagnostics());

            return new RecentLogLocalState(
                true,
                AndroidAppLogArchive.Load(activity),
                AndroidWorkProfileDiagnosticsReader.Read(activity));
        }, cancellationToken);
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
            ? await AndroidProfileCommandGateway.CheckWorkProfileOwnerAsync(commandRunner, cancellationToken)
                .ConfigureAwait(false)
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

    private static WorkProfileStateKind ResolveWorkProfileState(
        bool hadConfiguredWorkProfile,
        WorkProfileDiagnostics profileDiagnostics,
        WorkProfileOwnerCheckResult ownerCheck)
    {
        if (ownerCheck.Kind == WorkProfileOwnerCheckKind.AppIsProfileOwner)
            return WorkProfileStateKind.Available;

        if (hadConfiguredWorkProfile || HasAnyWorkProfileSignal(profileDiagnostics, ownerCheck))
            return WorkProfileStateKind.Unavailable;

        return WorkProfileStateKind.NoWorkProfile;
    }

    private static WorkProfileRecoveryKind ResolveWorkProfileRecoveryKind(
        WorkProfileStateKind workProfileState,
        WorkProfileDiagnostics profileDiagnostics,
        WorkProfileOwnerCheckResult ownerCheck)
    {
        if (workProfileState != WorkProfileStateKind.Unavailable)
            return WorkProfileRecoveryKind.None;

        if (IsProbablyDeletedManagedProfile(profileDiagnostics))
            return WorkProfileRecoveryKind.ProbablyDeletedRestartOnboarding;

        if (IsForeignManagedProfile(profileDiagnostics, ownerCheck))
            return WorkProfileRecoveryKind.DeleteWorkProfile;

        return ownerCheck.Kind switch
        {
            WorkProfileOwnerCheckKind.VersionUpdateFailed => WorkProfileRecoveryKind.UpdateFailedDeleteWorkProfile,
            WorkProfileOwnerCheckKind.AppInstalledButNotOwner => WorkProfileRecoveryKind.DeleteWorkProfile,
            _ => WorkProfileRecoveryKind.None
        };
    }

    private static bool IsProbablyDeletedManagedProfile(WorkProfileDiagnostics profileDiagnostics)
    {
        return profileDiagnostics.ManagedProfileExists
               && profileDiagnostics.QuietModeEnabled == true
               && profileDiagnostics.UserRunning == false
               && !profileDiagnostics.AvailableToCrossProfileApps
               && !profileDiagnostics.CommandTargetResolvable;
    }

    private static bool IsForeignManagedProfile(
        WorkProfileDiagnostics profileDiagnostics,
        WorkProfileOwnerCheckResult ownerCheck)
    {
        return profileDiagnostics.ManagedProfileExists
               && profileDiagnostics.UserRunning == true
               && profileDiagnostics.QuietModeEnabled != true
               && !profileDiagnostics.AvailableToCrossProfileApps
               && !profileDiagnostics.CommandTargetResolvable
               && ownerCheck.Kind == WorkProfileOwnerCheckKind.TargetUnavailable;
    }

    private static bool ShouldUpdateWorkProfileApp(long localVersionCode, WorkProfileOwnerCheckResult ownerCheck)
    {
        if (ownerCheck.Kind != WorkProfileOwnerCheckKind.AppIsProfileOwner)
            return false;

        return localVersionCode > 0 && ownerCheck.AppVersionCode > 0 && ownerCheck.AppVersionCode != localVersionCode;
    }

    private static long GetLocalVersionCode(PackageManager? packageManager, string? packageName)
    {
        if (packageManager is null || string.IsNullOrWhiteSpace(packageName)) return 0;

        try
        {
            return packageManager.GetPackageInfo(packageName, PackageInfoFlags.MatchAll)?.LongVersionCode ?? 0;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return 0;
        }
    }

    private static bool HasConfiguredWorkProfileSignal(LocalStorageManager storage)
    {
        return storage.GetBoolean(StorageKeys.HasSetup)
               || storage.GetBoolean(StorageKeys.OnboardingCompleted)
               || storage.GetBoolean(StorageKeys.IsSettingUp)
               || storage.GetLong(StorageKeys.ManagedProfileProvisionedAtUtc) > 0
               || !string.IsNullOrWhiteSpace(AuthenticationUtility.GetExistingKey());
    }

    private static bool HasAnyWorkProfileSignal(
        WorkProfileDiagnostics profileDiagnostics,
        WorkProfileOwnerCheckResult ownerCheck)
    {
        return profileDiagnostics.ManagedProfileExists
               || profileDiagnostics.AvailableToCrossProfileApps
               || profileDiagnostics.CommandTargetResolvable
               || ownerCheck.Kind is WorkProfileOwnerCheckKind.AuthenticationKeyMissing
                   or WorkProfileOwnerCheckKind.Unreachable
                   or WorkProfileOwnerCheckKind.VersionUpdateFailed
                   or WorkProfileOwnerCheckKind.AppInstalledButNotOwner;
    }

    private static void SynchronizeStorageWithResolvedState(WorkProfileStateKind workProfileState)
    {
        switch (workProfileState)
        {
            case WorkProfileStateKind.Available:
                AgnosiaUtilities.MarkWorkProfileReady();
                break;
            case WorkProfileStateKind.Unavailable:
                AgnosiaUtilities.MarkWorkProfileResetRequired();
                break;
            case WorkProfileStateKind.NoWorkProfile:
                AgnosiaUtilities.ClearWorkProfileConfiguredState();
                break;
        }
    }

    private static string BuildDiagnosticReason(
        WorkProfileStateKind state,
        WorkProfileDiagnostics profileDiagnostics,
        WorkProfileOwnerCheckResult ownerCheck)
    {
        return $"state={state}; {profileDiagnostics.ToLogString()}; " +
               $"ownerCheck={ownerCheck.Kind}; {ownerCheck.DiagnosticReason}";
    }

    private static WorkProfileDiagnostics CreateEmptyWorkProfileDiagnostics()
    {
        return new WorkProfileDiagnostics(
            false,
            false,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            0,
            0,
            "notRead");
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
                app.RiskyPermissions,
                app.MatchedPermissionRiskRuleIds,
                app.PermissionRiskScore,
                app.PermissionRiskRawScore,
                app.PermissionRiskConfidence,
                app.PermissionRiskScoreBreakdown,
                app.ManifestPermissions,
                app.RuntimePermissions,
                app.PermissionRiskAvailable,
                app.IsInternetBlocked);
        }

        return mappedApps;
    }

    private sealed record AppQueryResult(
        IReadOnlyList<AppServiceModel> Apps,
        IReadOnlyList<string> InteractionPackages)
    {
        public static AppQueryResult Empty { get; } = new([], []);
    }

    private sealed record DashboardProfileLocalState(
        bool IsSupported,
        AppSettingsSnapshot? Settings,
        bool HadConfiguredWorkProfile,
        WorkProfileDiagnostics? ProfileDiagnostics,
        long LocalVersionCode);

    private sealed record RecentLogLocalState(
        bool LoggingEnabled,
        IReadOnlyList<AppLogEntry> Logs,
        WorkProfileDiagnostics ProfileDiagnostics);
}
