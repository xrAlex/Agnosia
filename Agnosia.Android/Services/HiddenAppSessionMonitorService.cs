using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using _Microsoft.Android.Resource.Designer;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Notifications;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Infrastructure;
using Agnosia.Android.Receivers;
using Agnosia.Services;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using Math = System.Math;
using OperationCanceledException = System.OperationCanceledException;
using Stopwatch = System.Diagnostics.Stopwatch;
using StringBuilder = System.Text.StringBuilder;

namespace Agnosia.Android.Services;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
[MetaData("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE",
    Value = "monitor_hidden_work_profile_app_until_user_leaves_it")]
public sealed partial class HiddenAppSessionMonitorService : Service
{
    private const string LogTag = "AgnosiaHiddenSession";
    private const string PermissionControllerPackage = "com.google.android.permissioncontroller";
    private const string AospPermissionControllerPackage = "com.android.permissioncontroller";
    private const string SettingsPackage = "com.android.settings";
    private const string PackageInstallerPackage = "com.android.packageinstaller";
    private const string GoogleDocumentsUiPackage = "com.google.android.documentsui";
    private const string AospDocumentsUiPackage = "com.android.documentsui";
    private const string GooglePlayServicesPackage = "com.google.android.gms";
    private const string ActionStart = "agnosia.action.START_HIDDEN_APP_SESSION";
    private const string ExtraPackageName = "packageName";
    private const string ExtraDisplayName = "displayName";
    private const string ExtraTaskId = "taskId";
    private const string ExtraStartedAtUnixTimeMilliseconds = "startedAtUnixTimeMilliseconds";
    private const string ScreenLockPersistedReason = "screen_lock_persisted_session";
    private const string ScreenNonInteractiveReason = HiddenAppSessionMonitorStateMachine.ScreenNonInteractiveReason;
    private const string SessionReplacedReason = "session_replaced";
    private const int NotificationId = 0x57C31;
    private const string NotificationChannelId = "agnosia.hidden-app-session";
    private const string NotificationChannelName = "Сессии Agnosia";
    private const string NotificationChannelDescription = "Мониторинг скрытых приложений в рабочем профиле";
    private const int UsageEventMoveToForegroundOrActivityResumed = 1;
    private const int UsageEventMoveToBackgroundOrActivityPaused = 2;
    private const int UsageEventActivityStopped = 23;
    private const int UsageEventActivityDestroyed = 24;
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SteadyPollInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InitialLaunchGracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan InitialFastPollingWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PostLaunchTransientUiGracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan UserBackgroundHideDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SystemDelegatedUsageFallbackWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UsageEventsLookback = TimeSpan.FromMinutes(10);

    private readonly Lock _sync = new();
    private CancellationTokenSource? _monitorCts;
    private HiddenAppSessionState? _activeSession;
    private ComponentName? _adminComponent;
    private UsageObservationSnapshot? _lastUsageObservationSnapshot;
    private UsageSessionObservation? _lastUsageSessionObservation;
    private TaskObservationSnapshot? _lastTaskObservationSnapshot;
    private long _nextUsageEventsQueryBeginUnixTimeMilliseconds;
    private bool _usageEventsProblemWarningLogged;

    public static void StartMonitoring(
        Context context,
        string packageName,
        string displayName,
        int taskId,
        AndroidAppLaunchResult launchResult,
        PendingIntent? parentFrozenCallback = null)
    {
        Log.Info(LogTag, $"StartMonitoring requested for {packageName}, taskId={taskId}.");
        var intent = CreateCommandIntent(context, ActionStart, packageName, displayName, taskId, launchResult);
        if (parentFrozenCallback is not null)
            intent.PutExtra(AndroidCommandContract.ExtraParentFrozenCallback, parentFrozenCallback);

        AndroidServiceApi.TryStartForegroundService(
            context,
            intent,
            LogTag,
            $"Android не смог запустить монитор скрытого приложения {packageName}.");
    }

    public static bool CompletePersistedSessionForScreenLock(Context context)
    {
        if (!TryLoadPersistedSession(out var session))
        {
            Log.Info(LogTag, "No persisted hidden-app session to complete on screen lock.");
            return false;
        }

        try
        {
            if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } policyManager)
            {
                Log.Warn(LogTag,
                    $"DevicePolicyManager unavailable, could not complete persisted hidden-app session for {session.PackageName} on screen lock.");
                return false;
            }

            var admin = AgnosiaUtilities.GetAdminComponent(context, typeof(AgnosiaDeviceAdminReceiver));
            var hiddenApplied = policyManager.SetApplicationHidden(admin, session.PackageName, true);
            if (!hiddenApplied && !policyManager.IsApplicationHidden(admin, session.PackageName))
            {
                Log.Warn(LogTag,
                    $"Android did not confirm re-hiding {session.PackageName}. reason={ScreenLockPersistedReason}");
                return false;
            }

            PersistSession(null);
            var launchResult = GetSessionLaunchResult(session)
                .WithStage(AndroidAppLaunchStage.PackageRehidden, ScreenLockPersistedReason);
            launchResult.Log(LogTag);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag,
                $"Failed to complete persisted hidden-app session for {session.PackageName} on screen lock: {exception.Message}");
            return false;
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AgnosiaRuntime.Initialize(this);
        _adminComponent = AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Log.Debug(LogTag, $"OnStartCommand action={intent?.Action ?? "<null>"} startId={startId}.");
        try
        {
            var action = intent?.Action;
            HiddenAppSessionState session;
            if (string.Equals(action, ActionStart, StringComparison.Ordinal))
            {
                if (!TryReadSession(intent, out session))
                {
                    StopSelf();
                    return StartCommandResult.NotSticky;
                }
            }
            else if (!TryLoadPersistedSession(out session))
            {
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            StartOrReplaceSession(session);
            return StartCommandResult.Sticky;
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to process monitor service start: {exception}");
            StopSelf();
            return StartCommandResult.NotSticky;
        }
    }

    public override void OnDestroy()
    {
        lock (_sync)
        {
            CancelMonitorLocked();
        }

        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        HiddenAppSessionState? session;
        lock (_sync)
        {
            session = _activeSession;
        }

        if (session is not null)
        {
            Log.Info(LogTag, $"Task removed by user for {session.PackageName}, taskId={session.TaskId}.");
            CompleteSession(session, "task_removed");
        }

        base.OnTaskRemoved(rootIntent);
    }

    private void StartOrReplaceSession(HiddenAppSessionState session)
    {
        HiddenAppSessionState? previousSession;
        lock (_sync)
        {
            previousSession = _activeSession;
            _activeSession = session;
            PersistSession(session);
            CancelMonitorLocked();
            _monitorCts = new CancellationTokenSource();
            _ = Task.Run(() => MonitorSessionSafelyAsync(session, _monitorCts.Token));
        }

        _lastUsageObservationSnapshot = null;
        _lastUsageSessionObservation = null;
        _lastTaskObservationSnapshot = null;
        _nextUsageEventsQueryBeginUnixTimeMilliseconds = GetSessionStartedAt(session)
            .AddSeconds(-2)
            .ToUnixTimeMilliseconds();
        _usageEventsProblemWarningLogged = false;

        if (previousSession is not null
            && !string.Equals(previousSession.PackageName, session.PackageName, StringComparison.Ordinal))
        {
            Log.Debug(
                LogTag,
                $"Replacing active hidden-session. previous={previousSession.PackageName}, next={session.PackageName}, previousTaskId={previousSession.TaskId}, nextTaskId={session.TaskId}.");
            TryHidePackage(previousSession, SessionReplacedReason);
        }

        if (!AndroidUsageStatsAccessApi.HasAccess(this, LogTag, false, false))
        {
            var updatedLaunchResult = GetSessionLaunchResult(session)
                .WithIssue(AndroidAppLaunchIssueKind.UsageAccessDenied, "monitor_usage_access=denied");
            updatedLaunchResult.Log(LogTag);
            session = session with { LaunchResult = updatedLaunchResult };
            lock (_sync)
            {
                if (_activeSession is not null && Matches(_activeSession, session))
                {
                    _activeSession = session;
                    PersistSession(session);
                }
            }
        }

        StartForegroundServiceNotification(session);
        Log.Info(
            LogTag,
            $"Started hidden-session monitor for {session.PackageName}, taskId={session.TaskId}, startedAt={GetSessionStartedAt(session):O}, fastPollMs={FastPollInterval.TotalMilliseconds}, steadyPollMs={SteadyPollInterval.TotalMilliseconds}, hideDelayMs={UserBackgroundHideDelay.TotalMilliseconds}.");
    }

    private async Task MonitorSessionSafelyAsync(HiddenAppSessionState session, CancellationToken cancellationToken)
    {
        try
        {
            await MonitorSessionAsync(session, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Hidden-session monitor failed for {session.PackageName}: {exception}");
        }
    }

    private async Task MonitorSessionAsync(HiddenAppSessionState session, CancellationToken cancellationToken)
    {
        var startedAt = GetSessionStartedAt(session);
        var stateMachine = new HiddenAppSessionMonitorStateMachine(
            startedAt,
            InitialLaunchGracePeriod,
            PostLaunchTransientUiGracePeriod,
            UserBackgroundHideDelay,
            InitialFastPollingWindow,
            FastPollInterval,
            SteadyPollInterval,
            IdlePollInterval);
        Log.Debug(
            LogTag,
            $"Monitor loop initialized. package={session.PackageName}, taskId={session.TaskId}, startedAt={startedAt:O}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var pollStartedAt = Stopwatch.GetTimestamp();
            var observation = ObserveSession(session, startedAt, now);
            var pollElapsedMs = Stopwatch.GetElapsedTime(pollStartedAt).TotalMilliseconds;
            var transition = stateMachine.MoveNext(now, IsDeviceInteractive(), observation);
            Log.Debug(
                LogTag,
                $"UsagePoll elapsedMs={pollElapsedMs:0.0}; package={session.PackageName}; foreground={observation.IsForeground}; inactive={observation.ConfirmedInactive}; delegated={observation.IsSystemDelegatedFlow}; statePhase={transition.Phase}; stateDecision={transition.DecisionReason}; stateAction={transition.Action}.");
            if (transition.TargetForegroundFirstSeen)
            {
                Log.Debug(LogTag,
                    $"Target foreground evidence observed. package={session.PackageName}, now={now:O}, top={observation.TopPackage ?? "<none>"}.");
                session = UpdateLaunchResult(
                    session,
                    result => result.WithStage(
                        AndroidAppLaunchStage.TargetBecameForeground,
                        $"top={observation.TopPackage ?? "<none>"}"));
            }

            if (transition.ResetInactiveSince is not null)
            {
                Log.Debug(LogTag,
                    $"Inactive timer reset. package={session.PackageName}, previousInactiveSince={transition.ResetInactiveSince:O}, now={now:O}, top={observation.TopPackage ?? "<none>"}, reason={transition.DecisionReason}.");
            }

            if (transition.ShouldRaiseLaunchObservationWarning)
            {
                Log.Warn(
                    LogTag,
                    $"Session {session.PackageName} has not produced foreground evidence yet; keeping it visible instead of hiding on an unconfirmed timeout.");
            }

            if (transition.ShouldRaiseTransientUiWarning)
            {
                Log.Warn(
                    LogTag,
                    $"Session {session.PackageName} has no current foreground evidence, but inactivity is not confirmed; keeping it visible.");
            }

            if (transition.Action == HiddenAppSessionTransitionAction.Complete)
            {
                if (transition.CompletionKind == HiddenAppSessionCompletionKind.AfterTargetTaskRecheck
                    && HasTargetTaskAtCompletion(session, observation, transition))
                {
                    stateMachine.PostponeCompletionBecauseTargetTaskStillPresent(now);
                    continue;
                }

                Log.Info(
                    LogTag,
                    $"Freeze decision: freeze. package={session.PackageName}, top={observation.TopPackage ?? "<none>"}, inactiveSince={FormatTime(transition.InactiveSince)}, inactiveForMs={transition.InactiveFor?.TotalMilliseconds ?? 0:0}, reason={transition.CompletionReason ?? "<none>"}, decisionReason={transition.DecisionReason}.");
                CompleteSession(session, transition.CompletionReason ?? "state_machine_completed");
                return;
            }

            try
            {
                await Task.Delay(transition.PollDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private bool HasTargetTaskAtCompletion(
        HiddenAppSessionState session,
        SessionObservation observation,
        HiddenAppSessionTransition transition)
    {
        var finalTaskObservation = ObserveTask(session);
        if (finalTaskObservation is null || !TaskBelongsToTarget(finalTaskObservation, session.PackageName))
        {
            Log.Info(
                LogTag,
                $"FreezeCandidate package={session.PackageName}, latestTop={observation.TopPackage ?? "<none>"}, inactiveSince={FormatTime(transition.InactiveSince)}, inactiveForMs={transition.InactiveFor?.TotalMilliseconds ?? 0:0}, hideDelayMs={UserBackgroundHideDelay.TotalMilliseconds:0}, taskExists=False, decision=freeze, reason=confirmed_inactivity_without_target_task.");
            return false;
        }

        Log.Info(
            LogTag,
            $"FreezeCandidate package={session.PackageName}, latestTop={observation.TopPackage ?? "<none>"}, inactiveSince={FormatTime(transition.InactiveSince)}, inactiveForMs={transition.InactiveFor?.TotalMilliseconds ?? 0:0}, hideDelayMs={UserBackgroundHideDelay.TotalMilliseconds:0}, taskExists=True, taskId={finalTaskObservation.TaskId}, taskBase={finalTaskObservation.BaseActivity ?? "<none>"}, taskTop={finalTaskObservation.TopActivity ?? "<none>"}, decision=keep_alive, reason=target_task_still_present.");
        return true;
    }

    private SessionObservation ObserveSession(HiddenAppSessionState session, DateTimeOffset startedAt,
        DateTimeOffset now)
    {
        var usageObservation = ObserveUsageEvents(session.PackageName, startedAt, now);
        var taskObservation = ObserveTask(session);

        if (taskObservation?.IsSystemDelegatedFlow == true)
            return new SessionObservation(
                false,
                taskObservation.TopPackage,
                false,
                null,
                true,
                true);

        if (taskObservation is not null
            && TaskBelongsToTarget(taskObservation, session.PackageName))
            return new SessionObservation(
                true,
                taskObservation.TopPackage ?? taskObservation.BasePackage,
                false,
                null,
                true,
                false);

        var observation = new SessionObservation(
            usageObservation?.IsForeground == true,
            usageObservation?.TopPackage,
            usageObservation?.IsForeground == false && usageObservation.ConfirmedInactive,
            usageObservation?.InactiveSince,
            usageObservation?.SawTargetForeground == true,
            usageObservation?.IsSystemDelegatedFlow == true);
        return observation;
    }

    private static bool TaskBelongsToTarget(TaskSessionObservation? observation, string packageName)
    {
        return observation is not null
               && (string.Equals(observation.BasePackage, packageName, StringComparison.Ordinal)
                   || string.Equals(observation.TopPackage, packageName, StringComparison.Ordinal));
    }

    private bool IsDeviceInteractive()
    {
        return AndroidSystemApi.GetPowerManager(this)?.IsInteractive != false;
    }

    private TaskSessionObservation? ObserveTask(HiddenAppSessionState session)
    {
        if (AndroidSystemApi.GetActivityManager(this) is not { } activityManager) return null;

        try
        {
            var appTasks = activityManager.AppTasks;
            if (appTasks is null) return null;

            foreach (var appTask in appTasks)
            {
                if (appTask.TaskInfo is not { } taskInfo || taskInfo.Id != session.TaskId)
                    continue;

                var baseActivity = taskInfo.BaseActivity;
                var topActivity = taskInfo.TopActivity;
                var basePackage = baseActivity?.PackageName;
                var topPackage = topActivity?.PackageName;
                var topClass = topActivity?.ClassName;
                var isSystemDelegatedFlow = string.Equals(basePackage, session.PackageName, StringComparison.Ordinal)
                                            && IsSystemDelegatedFlow(topPackage, topClass);
                var observation = new TaskSessionObservation(
                    session.TaskId,
                    basePackage,
                    baseActivity?.FlattenToShortString(),
                    topPackage,
                    topActivity?.FlattenToShortString(),
                    isSystemDelegatedFlow);

                LogTaskObservationIfChanged(session.PackageName, observation);
                return observation;
            }

            LogTaskObservationIfChanged(
                session.PackageName,
                new TaskSessionObservation(session.TaskId, null, null, null, null, false));
            return null;
        }
        catch (Exception exception)
        {
            Log.Debug(LogTag,
                $"Task observation unavailable for {session.PackageName}, taskId={session.TaskId}: {exception.Message}");
            return null;
        }
    }

    private void CompleteSession(HiddenAppSessionState session, string reason)
    {
        var shouldComplete = false;
        lock (_sync)
        {
            if (_activeSession is not null && Matches(_activeSession, session))
            {
                shouldComplete = true;
                _activeSession = null;
                PersistSession(null);
                CancelMonitorLocked();
            }
        }

        if (!shouldComplete) return;

        TryHidePackage(session, reason);
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    private void TryHidePackage(HiddenAppSessionState session, string reason)
    {
        try
        {
            if (AndroidSystemApi.GetDevicePolicyManager(this) is not { } policyManager)
            {
                Log.Warn(LogTag, $"DevicePolicyManager unavailable, could not hide {session.PackageName} again.");
                return;
            }

            var admin = _adminComponent ??=
                AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
            var hiddenApplied = policyManager.SetApplicationHidden(admin, session.PackageName, true);
            if (!hiddenApplied && !policyManager.IsApplicationHidden(admin, session.PackageName))
            {
                Log.Warn(LogTag, $"Android did not confirm re-hiding {session.PackageName}. reason={reason}");
                return;
            }

            var launchResult = GetSessionLaunchResult(session)
                .WithStage(AndroidAppLaunchStage.PackageRehidden, reason);
            launchResult.Log(LogTag);
            if (string.Equals(reason, SessionReplacedReason, StringComparison.Ordinal))
            {
                Log.Debug(LogTag,
                    $"Skipping VPN enable after replacing {session.PackageName}; another hidden-app session is active.");
                return;
            }

            if (string.Equals(reason, ScreenNonInteractiveReason, StringComparison.Ordinal))
            {
                Log.Debug(LogTag,
                    $"Skipping session-level VPN enable after screen-lock freeze for {session.PackageName}; lock service handles it.");
                return;
            }

            if (TryNotifyParentWithPendingIntent(session, reason)) return;

            Log.Debug(LogTag, $"Notifying parent profile about frozen app {session.PackageName}. reason={reason}");
            var result = AndroidProfileCommandGateway.NotifyParentWorkAppFrozen(
                this,
                $"session_hide:{reason}:{session.PackageName}");
            if (!result.Succeeded)
            {
                Log.Warn(LogTag,
                    $"Could not notify parent profile about frozen app {session.PackageName}: {result.Message}");
            }
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to hide {session.PackageName} again: {exception}");
        }
    }

    private bool TryNotifyParentWithPendingIntent(HiddenAppSessionState session, string reason)
    {
        if (session.ParentFrozenCallback is not { } callback)
        {
            Log.Debug(LogTag,
                $"No parent pending-intent callback is available for {session.PackageName}; falling back to cross-profile activity.");
            return false;
        }

        try
        {
            Log.Debug(LogTag,
                $"Sending parent pending-intent callback for frozen app {session.PackageName}. reason={reason}");
            callback.Send(
                this,
                Result.Ok,
                null,
                null,
                null,
                null,
                AndroidPendingIntentApi.CreateSenderBackgroundActivityStartOptions());
            return true;
        }
        catch (PendingIntent.CanceledException exception)
        {
            Log.Warn(LogTag,
                $"Parent pending-intent callback was canceled for {session.PackageName}: {exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Parent pending-intent callback failed for {session.PackageName}: {exception.Message}");
            return false;
        }
    }

    private static Intent CreateCommandIntent(
        Context context,
        string action,
        string packageName,
        string displayName,
        int taskId,
        AndroidAppLaunchResult launchResult)
    {
        var intent = new Intent(context, typeof(HiddenAppSessionMonitorService));
        intent.SetAction(action);
        intent.PutExtra(ExtraPackageName, packageName);
        intent.PutExtra(ExtraDisplayName, displayName);
        intent.PutExtra(ExtraTaskId, taskId);
        intent.PutExtra(ExtraStartedAtUnixTimeMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        launchResult.WriteToIntent(intent);
        return intent;
    }

    private static bool TryReadSession(Intent? intent, out HiddenAppSessionState session)
    {
        var packageName = intent?.GetStringExtra(ExtraPackageName);
        var displayName = intent?.GetStringExtra(ExtraDisplayName);
        var taskId = intent?.GetIntExtra(ExtraTaskId, -1) ?? -1;
        var startedAt = intent?.GetLongExtra(ExtraStartedAtUnixTimeMilliseconds, 0) ?? 0;

        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(displayName) || taskId < 0)
        {
            session = HiddenAppSessionState.Empty;
            return false;
        }

        var launchResult = AndroidAppLaunchResult.TryRead(intent, out var restoredLaunchResult)
            ? restoredLaunchResult.WithDisplayName(displayName)
            : AndroidAppLaunchResult.CommandReceived(packageName, displayName);
        session = new HiddenAppSessionState(
            packageName,
            displayName,
            taskId,
            startedAt > 0 ? startedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            launchResult)
        {
            ParentFrozenCallback = AndroidIntentExtras.ReadParentFrozenCallback(intent)
        };
        return true;
    }

    private static bool Matches(HiddenAppSessionState left, HiddenAppSessionState right)
    {
        return string.Equals(left.PackageName, right.PackageName, StringComparison.Ordinal)
               && left.TaskId == right.TaskId;
    }

    private static DateTimeOffset GetSessionStartedAt(HiddenAppSessionState session)
    {
        return session.StartedAtUnixTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(session.StartedAtUnixTimeMilliseconds)
            : DateTimeOffset.UtcNow;
    }

    private UsageSessionObservation? ObserveUsageEvents(
        string packageName,
        DateTimeOffset startedAt,
        DateTimeOffset now)
    {
        if (!AndroidUsageStatsAccessApi.HasAccess(this, LogTag, false, false))
        {
            WarnUsageEventsProblemOnce(
                "Usage stats access is not granted in the work profile; no foreground evidence can be produced.");
            return null;
        }

        if (AndroidSystemApi.GetUsageStatsManager(this) is not { } usageStatsManager)
        {
            WarnUsageEventsProblemOnce("UsageStatsManager unavailable; no inactive evidence was produced.");
            return null;
        }

        try
        {
            var begin = Math.Max(
                Math.Max(
                    startedAt.AddSeconds(-2).ToUnixTimeMilliseconds(),
                    now.Subtract(UsageEventsLookback).ToUnixTimeMilliseconds()),
                _nextUsageEventsQueryBeginUnixTimeMilliseconds);
            var events = usageStatsManager.QueryEvents(begin, now.ToUnixTimeMilliseconds());
            if (events is null)
            {
                WarnUsageEventsProblemOnce("Usage events query returned null; no inactive evidence was produced.");
                return null;
            }

            var usageEvent = new UsageEvents.Event();
            var scannedEvents = 0;
            var targetEvents = 0;
            var foregroundEvents = 0;
            var sawTargetForeground = false;
            var latestTargetEventType = -1;
            var latestTargetEventAt = 0L;
            string? latestForegroundPackage = null;
            string? latestTargetClassName = null;
            string? latestTargetEventName = null;
            string? latestForegroundClassName = null;
            string? latestForegroundEventName = null;
            var latestForegroundAt = 0L;
            var latestScannedEventAt = 0L;
            var targetUsageEvents = new StringBuilder();

            while (events.HasNextEvent)
            {
                if (!events.GetNextEvent(usageEvent)) break;

                scannedEvents++;
                var eventType = (int)usageEvent.EventType;
                var eventPackage = usageEvent.PackageName;
                var eventClassName = usageEvent.ClassName;
                latestScannedEventAt = Math.Max(latestScannedEventAt, usageEvent.TimeStamp);
                if (IsUsageForegroundEvent(eventType))
                {
                    foregroundEvents++;
                    latestForegroundPackage = eventPackage;
                    latestForegroundClassName = eventClassName;
                    latestForegroundEventName = GetUsageEventName(eventType);
                    latestForegroundAt = usageEvent.TimeStamp;
                }

                if (!string.Equals(eventPackage, packageName, StringComparison.Ordinal)) continue;

                targetEvents++;
                if (IsUsageForegroundEvent(eventType)) sawTargetForeground = true;

                if (IsUsageForegroundEvent(eventType) || IsUsageInactiveEvent(eventType))
                {
                    latestTargetEventType = eventType;
                    latestTargetEventAt = usageEvent.TimeStamp;
                    latestTargetClassName = eventClassName;
                    latestTargetEventName = GetUsageEventName(eventType);
                    AppendUsageEventTrace(targetUsageEvents, latestTargetEventName, eventClassName, usageEvent.TimeStamp);
                }
            }

            UsageSessionObservation observation;
            string reason;
            var hasSeenTargetForeground =
                sawTargetForeground || _lastUsageSessionObservation?.SawTargetForeground == true;
            if (latestTargetEventType < 0)
            {
                if (_lastUsageSessionObservation is { IsSystemDelegatedFlow: true } previousDelegatedObservation
                    && latestForegroundAt > 0
                    && !string.IsNullOrWhiteSpace(latestForegroundPackage)
                    && !string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal)
                    && !IsSystemDelegatedFlow(latestForegroundPackage, latestForegroundClassName))
                {
                    observation = new UsageSessionObservation(
                        false,
                        true,
                        previousDelegatedObservation.SawTargetForeground || sawTargetForeground,
                        DateTimeOffset.FromUnixTimeMilliseconds(latestForegroundAt),
                        latestForegroundPackage,
                        false);
                    reason = "delegated_flow_exited_to_successor_foreground";
                }
                else if (_lastUsageSessionObservation is { } previousObservation
                    && TryResolvePendingInactiveObservation(
                        previousObservation,
                        latestForegroundPackage,
                        latestForegroundAt,
                        packageName,
                        out observation,
                        out reason))
                {
                    observation = observation with
                    {
                        SawTargetForeground = observation.SawTargetForeground || sawTargetForeground
                    };
                }
                else
                {
                    observation = _lastUsageSessionObservation is { } previousObservationForCarry
                        ? previousObservationForCarry with
                        {
                            SawTargetForeground = previousObservationForCarry.SawTargetForeground || sawTargetForeground,
                            TopPackage = latestForegroundPackage ?? previousObservationForCarry.TopPackage
                        }
                        : new UsageSessionObservation(false, false, sawTargetForeground, null, latestForegroundPackage, false);
                    reason = "no_target_lifecycle_event";
                }
            }
            else if (IsUsageForegroundEvent(latestTargetEventType))
            {
                observation = new UsageSessionObservation(true, false, true, null, packageName, false);
                reason = "target_latest_event_foreground";
            }
            else if (string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal)
                     && latestForegroundAt >= latestTargetEventAt)
            {
                observation = new UsageSessionObservation(true, false, true, null, packageName, false);
                reason = "target_is_latest_foreground";
            }
            else if (IsRecentSystemDelegatedUsageForeground(
                         latestForegroundPackage,
                         latestForegroundClassName,
                         latestForegroundAt,
                         latestTargetEventType,
                         latestTargetEventAt,
                         hasSeenTargetForeground))
            {
                observation =
                    new UsageSessionObservation(false, false, hasSeenTargetForeground, null, latestForegroundPackage, true);
                reason = "system_delegated_usage_foreground";
            }
            else if (IsTransientSystemPackage(latestForegroundPackage))
            {
                observation =
                    new UsageSessionObservation(true, false, sawTargetForeground, null, latestForegroundPackage, false);
                reason = "transient_system_ui_foreground";
            }
            else if (IsUsageInactiveEvent(latestTargetEventType)
                     && string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal))
            {
                observation =
                    new UsageSessionObservation(false, false, sawTargetForeground, null, latestForegroundPackage, false);
                reason = "target_inactive_but_top_still_target";
            }
            else if (IsUsageInactiveEvent(latestTargetEventType)
                     && latestForegroundAt > latestTargetEventAt
                     && !string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal))
            {
                observation = new UsageSessionObservation(
                    false,
                    true,
                    sawTargetForeground,
                    latestTargetEventAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(latestTargetEventAt) : null,
                    latestForegroundPackage,
                    false);
                reason = "target_latest_event_inactive";
            }
            else if (IsUsageInactiveEvent(latestTargetEventType))
            {
                var inactiveSince = latestTargetEventAt > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(latestTargetEventAt)
                    : (DateTimeOffset?)null;
                observation = new UsageSessionObservation(
                    false,
                    true,
                    sawTargetForeground,
                    inactiveSince,
                    latestForegroundPackage,
                    false);
                reason = "target_activity_inactive_without_successor_foreground";
            }
            else
            {
                observation =
                    new UsageSessionObservation(false, false, sawTargetForeground, null, latestForegroundPackage, false);
                reason = "target_latest_event_unknown";
            }

            LogUsageObservationIfChanged(
                packageName,
                begin,
                now,
                scannedEvents,
                targetEvents,
                foregroundEvents,
                latestTargetEventName,
                latestTargetEventAt,
                latestTargetClassName,
                latestForegroundPackage,
                latestForegroundEventName,
                latestForegroundClassName,
                latestForegroundAt,
                targetUsageEvents.ToString(),
                observation,
                reason);
            _lastUsageSessionObservation = observation;
            _nextUsageEventsQueryBeginUnixTimeMilliseconds = Math.Max(
                begin,
                latestScannedEventAt > 0
                    ? latestScannedEventAt - 1
                    : now.AddSeconds(-1).ToUnixTimeMilliseconds());
            return observation;
        }
        catch (Exception exception)
        {
            WarnUsageEventsProblemOnce(
                $"Usage events query failed for {packageName}; no inactive evidence was produced. error={exception.Message}");
            return null;
        }
    }

    private void WarnUsageEventsProblemOnce(string message)
    {
        if (_usageEventsProblemWarningLogged) return;

        _usageEventsProblemWarningLogged = true;
        Log.Warn(LogTag, message);
    }

    private static bool TryResolvePendingInactiveObservation(
        UsageSessionObservation previousObservation,
        string? latestForegroundPackage,
        long latestForegroundAt,
        string packageName,
        out UsageSessionObservation observation,
        out string reason)
    {
        observation = previousObservation;
        reason = string.Empty;
        if (previousObservation.IsForeground
            || previousObservation.ConfirmedInactive
            || previousObservation.InactiveSince is null)
        {
            return false;
        }

        if (latestForegroundAt >= previousObservation.InactiveSince.Value.ToUnixTimeMilliseconds()
            && !string.IsNullOrWhiteSpace(latestForegroundPackage)
            && !string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal)
            && !IsTransientSystemPackage(latestForegroundPackage))
        {
            observation = previousObservation with
            {
                ConfirmedInactive = true,
                TopPackage = latestForegroundPackage
            };
            reason = "target_inactive_then_successor_foreground";
            return true;
        }

        return false;
    }

    private void LogUsageObservationIfChanged(
        string packageName,
        long queryBegin,
        DateTimeOffset queryEnd,
        int scannedEvents,
        int targetEvents,
        int foregroundEvents,
        string? latestTargetEventName,
        long latestTargetEventAt,
        string? latestTargetClassName,
        string? latestForegroundPackage,
        string? latestForegroundEventName,
        string? latestForegroundClassName,
        long latestForegroundAt,
        string targetUsageEvents,
        UsageSessionObservation observation,
        string reason)
    {
        var snapshot = new UsageObservationSnapshot(
            observation.IsForeground,
            observation.ConfirmedInactive,
            observation.SawTargetForeground,
            observation.TopPackage,
            observation.IsSystemDelegatedFlow,
            observation.InactiveSince,
            latestTargetEventName,
            latestTargetEventAt,
            latestTargetClassName,
            latestForegroundPackage,
            latestForegroundEventName,
            latestForegroundClassName,
            latestForegroundAt,
            targetUsageEvents,
            scannedEvents,
            targetEvents,
            foregroundEvents,
            reason);
        if (snapshot.Equals(_lastUsageObservationSnapshot)) return;

        _lastUsageObservationSnapshot = snapshot;
        Log.Debug(
            LogTag,
            $"Usage observation changed. package={packageName}, reason={reason}, queryBegin={DateTimeOffset.FromUnixTimeMilliseconds(queryBegin):O}, queryEnd={queryEnd:O}, scanned={scannedEvents}, targetEvents={targetEvents}, foregroundEvents={foregroundEvents}, targetUsageEvents=[{FormatTrace(targetUsageEvents)}], latestTarget={latestTargetEventName ?? "<none>"}:{latestTargetClassName ?? "<none>"}@{FormatUnixTime(latestTargetEventAt)}, latestForeground={latestForegroundPackage ?? "<none>"}:{latestForegroundEventName ?? "<none>"}:{latestForegroundClassName ?? "<none>"}@{FormatUnixTime(latestForegroundAt)}, resultForeground={observation.IsForeground}, resultInactive={observation.ConfirmedInactive}, resultDelegated={observation.IsSystemDelegatedFlow}, sawTargetForeground={observation.SawTargetForeground}, inactiveSince={FormatTime(observation.InactiveSince)}, top={observation.TopPackage ?? "<none>"}.");
    }

    private void LogTaskObservationIfChanged(string packageName, TaskSessionObservation observation)
    {
        var snapshot = new TaskObservationSnapshot(
            observation.TaskId,
            observation.BasePackage,
            observation.BaseActivity,
            observation.TopPackage,
            observation.TopActivity,
            observation.IsSystemDelegatedFlow);
        if (snapshot.Equals(_lastTaskObservationSnapshot)) return;

        _lastTaskObservationSnapshot = snapshot;
        if (observation.IsSystemDelegatedFlow)
        {
            Log.Info(
                LogTag,
                $"System delegated flow detected, freeze postponed. package={packageName}, taskId={observation.TaskId}, base={observation.BaseActivity ?? "<none>"}, top={observation.TopActivity ?? "<none>"}, decision=keep_alive.");
            return;
        }

        Log.Debug(
            LogTag,
            $"Task observation changed. package={packageName}, taskId={observation.TaskId}, base={observation.BaseActivity ?? "<none>"}, top={observation.TopActivity ?? "<none>"}, systemDelegatedFlow={observation.IsSystemDelegatedFlow}.");
    }

    private static bool IsUsageForegroundEvent(int eventType)
    {
        return eventType == UsageEventMoveToForegroundOrActivityResumed;
    }

    private static bool IsUsageInactiveEvent(int eventType)
    {
        return eventType == UsageEventMoveToBackgroundOrActivityPaused;
    }

    private static string GetUsageEventName(int eventType)
    {
        return eventType switch
        {
            UsageEventMoveToForegroundOrActivityResumed => "FOREGROUND_OR_RESUMED",
            UsageEventMoveToBackgroundOrActivityPaused => "BACKGROUND_OR_PAUSED",
            UsageEventActivityStopped => "ACTIVITY_STOPPED",
            UsageEventActivityDestroyed => "ACTIVITY_DESTROYED",
            _ => eventType.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static bool IsTransientSystemPackage(string? packageName)
    {
        return string.Equals(packageName, PermissionControllerPackage, StringComparison.Ordinal)
               || string.Equals(packageName, AospPermissionControllerPackage, StringComparison.Ordinal)
               || string.Equals(packageName, GooglePlayServicesPackage, StringComparison.Ordinal);
    }

    private static bool IsSystemDelegatedFlow(string? packageName, string? className)
    {
        return string.Equals(packageName, SettingsPackage, StringComparison.Ordinal)
               || string.Equals(packageName, PermissionControllerPackage, StringComparison.Ordinal)
               || string.Equals(packageName, AospPermissionControllerPackage, StringComparison.Ordinal)
               || string.Equals(packageName, PackageInstallerPackage, StringComparison.Ordinal)
               || string.Equals(packageName, GoogleDocumentsUiPackage, StringComparison.Ordinal)
               || string.Equals(packageName, AospDocumentsUiPackage, StringComparison.Ordinal)
               || IsKnownSystemDelegatedActivity(className);
    }

    private static bool IsRecentSystemDelegatedUsageForeground(
        string? foregroundPackageName,
        string? foregroundClassName,
        long foregroundAtUnixTimeMilliseconds,
        int latestTargetEventType,
        long latestTargetEventAtUnixTimeMilliseconds,
        bool hasSeenTargetForeground)
    {
        if (!hasSeenTargetForeground
            || !IsUsageInactiveEvent(latestTargetEventType)
            || latestTargetEventAtUnixTimeMilliseconds <= 0
            || foregroundAtUnixTimeMilliseconds < latestTargetEventAtUnixTimeMilliseconds
            || !IsSystemDelegatedFlow(foregroundPackageName, foregroundClassName))
        {
            return false;
        }

        var elapsed = TimeSpan.FromMilliseconds(
            foregroundAtUnixTimeMilliseconds - latestTargetEventAtUnixTimeMilliseconds);
        return elapsed <= SystemDelegatedUsageFallbackWindow;
    }

    private static bool IsKnownSystemDelegatedActivity(string? className)
    {
        return !string.IsNullOrWhiteSpace(className)
               && (className.Contains("AppNotificationSettingsActivity", StringComparison.Ordinal)
                   || className.Contains("Permission", StringComparison.Ordinal)
                   || className.Contains("PackageInstaller", StringComparison.Ordinal)
                   || className.Contains("DocumentsActivity", StringComparison.Ordinal));
    }

    private static string FormatTime(DateTimeOffset? value)
    {
        return value is null ? "<none>" : value.Value.ToString("O");
    }

    private static string FormatUnixTime(long unixTimeMilliseconds)
    {
        return unixTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToString("O")
            : "<none>";
    }

    private static void AppendUsageEventTrace(
        StringBuilder builder,
        string eventName,
        string? className,
        long unixTimeMilliseconds)
    {
        if (builder.Length > 0) builder.Append(", ");

        builder
            .Append(eventName)
            .Append(':')
            .Append(string.IsNullOrWhiteSpace(className) ? "<none>" : className)
            .Append('@')
            .Append(FormatUnixTime(unixTimeMilliseconds));
    }

    private static string FormatTrace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
    }

    private void CancelMonitorLocked()
    {
        if (_monitorCts is null) return;

        _monitorCts.Cancel();
        _monitorCts.Dispose();
        _monitorCts = null;
    }

    private HiddenAppSessionState UpdateLaunchResult(
        HiddenAppSessionState session,
        Func<AndroidAppLaunchResult, AndroidAppLaunchResult> update)
    {
        var updatedResult = update(GetSessionLaunchResult(session));
        updatedResult.Log(LogTag);
        var updatedSession = session with { LaunchResult = updatedResult };
        lock (_sync)
        {
            if (_activeSession is not null && Matches(_activeSession, session))
            {
                _activeSession = updatedSession;
                PersistSession(updatedSession);
            }
        }

        return updatedSession;
    }

    private sealed record UsageSessionObservation(
        bool IsForeground,
        bool ConfirmedInactive,
        bool SawTargetForeground,
        DateTimeOffset? InactiveSince,
        string? TopPackage,
        bool IsSystemDelegatedFlow);

    private sealed record TaskSessionObservation(
        int TaskId,
        string? BasePackage,
        string? BaseActivity,
        string? TopPackage,
        string? TopActivity,
        bool IsSystemDelegatedFlow);

    private sealed record UsageObservationSnapshot(
        bool IsForeground,
        bool ConfirmedInactive,
        bool SawTargetForeground,
        string? TopPackage,
        bool IsSystemDelegatedFlow,
        DateTimeOffset? InactiveSince,
        string? LatestTargetEventName,
        long LatestTargetEventAt,
        string? LatestTargetClassName,
        string? LatestForegroundPackage,
        string? LatestForegroundEventName,
        string? LatestForegroundClassName,
        long LatestForegroundAt,
        string TargetUsageEvents,
        int ScannedEvents,
        int TargetEvents,
        int ForegroundEvents,
        string Reason);

    private sealed record TaskObservationSnapshot(
        int TaskId,
        string? BasePackage,
        string? BaseActivity,
        string? TopPackage,
        string? TopActivity,
        bool IsSystemDelegatedFlow);
}
