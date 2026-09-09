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


namespace Agnosia.Android.Services;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
[Property("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE",
    Value = "monitor_hidden_work_profile_app_until_user_leaves_it")]
public sealed partial class HiddenAppSessionMonitorService : Service
{
    private const string LogTag = "AgnosiaHiddenSession";
    private const string ActionStart = "agnosia.action.START_HIDDEN_APP_SESSION";
    private const string ActionRetryPendingHides = "agnosia.action.RETRY_PENDING_HIDDEN_APP_SESSIONS";
    private const string ExtraSessionId = "sessionId";
    private const string ExtraPackageName = "packageName";
    private const string ExtraDisplayName = "displayName";
    private const string ExtraTaskId = "taskId";
    private const string ExtraStartedAtUnixTimeMilliseconds = "startedAtUnixTimeMilliseconds";
    private const string ScreenNonInteractiveReason = HiddenAppSessionMonitorStateMachine.ScreenNonInteractiveReason;
    private const int NotificationId = 0x57C31;
    private const string NotificationChannelId = "agnosia.hidden-app-session";
    private const string NotificationChannelName = "Сессии Agnosia";
    private const string NotificationChannelDescription = "Мониторинг скрытых приложений в рабочем профиле";
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SteadyPollInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InitialLaunchGracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan InitialFastPollingWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PostLaunchTransientUiGracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan UserBackgroundHideDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UsageEventsLookback = TimeSpan.FromMinutes(10);

    private readonly Lock _sync = new();
    private CancellationTokenSource? _monitorCts;
    private CancellationTokenSource? _pendingHideRetryCts;
    private HiddenAppSessionStoreState _storeState = HiddenAppSessionStoreState.Empty;
    private ComponentName? _adminComponent;
    private HiddenAppUsageObservationReducer? _usageReducer;
#if DEBUG
    private DateTimeOffset _lastUsageDiagnosticAt;
#endif
    private UsageSessionObservation? _lastUsageSessionObservation;
    private long _nextUsageEventsQueryBeginUnixTimeMilliseconds;
    private bool _usageEventsProblemWarningLogged;

    internal static bool StartMonitoring(Context context, HiddenAppSessionState session)
    {
        Log.Info(LogTag, $"StartMonitoring requested for {session.PackageName}, taskId={session.TaskId}.");
        var intent = CreateCommandIntent(context, ActionStart, session);
        if (session.ParentFrozenCallback is not null)
            intent.PutExtra(AndroidCommandContract.ExtraParentFrozenCallback, session.ParentFrozenCallback);
        if (!string.IsNullOrWhiteSpace(session.ParentCallbackLaunchId))
            intent.PutExtra(AndroidCommandContract.ExtraCallbackLaunchId, session.ParentCallbackLaunchId);

        return AndroidServiceApi.TryStartForegroundService(
            context,
            intent,
            LogTag,
            $"Android не смог запустить монитор скрытого приложения {session.PackageName}.");
    }

    public static bool CompletePersistedSessionForScreenLock(Context context)
    {
        try
        {
            return CompletePersistedSessionForScreenLockCore(context);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to complete persisted hidden-app sessions on screen lock: {exception}");
            EnsurePendingHideRetryRunning(context);
            return false;
        }
    }

    private static bool CompletePersistedSessionForScreenLockCore(Context context)
    {
        if (!HiddenAppSessionConcurrency.TryEnterOperation(out var operation))
        {
            UpdatePersistedState(state => state.PrepareForScreenLock(DateTimeOffset.UtcNow));
            Log.Info(LogTag,
                "Screen-lock completion deferred while another hidden-app package operation is active.");
            EnsurePendingHideRetryRunning(context);
            return false;
        }

        using (operation)
        {
        if (!TryLoadPersistedState(out var state) || state.IsEmpty)
        {
            Log.Info(LogTag, "No persisted hidden-app session to complete on screen lock.");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        state = UpdatePersistedState(current => current.PrepareForScreenLock(now));
        ComponentName? admin = null;
        foreach (var pending in state.PendingHides.ToArray())
        {
            var outcome = TryHidePackage(context, pending, ref admin);
            state = UpdatePersistedState(current => outcome == HiddenAppHideAttemptResult.Failed
                ? current.RecordHideFailure(pending.Session.SessionId, DateTimeOffset.UtcNow)
                : current.ConfirmHidden(pending.Session.SessionId, DateTimeOffset.UtcNow));
            if (outcome != HiddenAppHideAttemptResult.Failed)
            {
                var launchResult = GetSessionLaunchResult(pending.Session)
                    .WithStage(AndroidAppLaunchStage.PackageRehidden, HiddenAppSessionStoreState.ScreenLockPersistedReason);
                launchResult.Log(LogTag);
            }
        }

        if (state.PendingHides.Length > 0)
        {
            EnsurePendingHideRetryRunning(context);
            return false;
        }

        return true;
        }
    }

    public static bool EnsurePendingHideRetryRunning(Context context)
    {
        var intent = new Intent(context, typeof(HiddenAppSessionMonitorService));
        intent.SetAction(ActionRetryPendingHides);
        return AndroidServiceApi.TryStartForegroundService(
            context,
            intent,
            LogTag,
            "Android не смог продолжить повторное скрытие рабочего приложения.");
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AgnosiaRuntime.Initialize(this);
        _adminComponent = AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
        TryLoadPersistedState(out _storeState);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Log.Debug(LogTag, $"OnStartCommand action={intent?.Action ?? "<null>"} startId={startId}.");
        try
        {
            var action = intent?.Action;
            if (string.Equals(action, ActionStart, StringComparison.Ordinal))
            {
                if (!TryReadSession(intent, out var session))
                {
                    StopSelf();
                    return StartCommandResult.NotSticky;
                }

                StartOrReplaceSession(session);
            }
            else
            {
                if (!TryLoadPersistedState(out var restoredState) || restoredState.IsEmpty)
                {
                    StopSelf();
                    return StartCommandResult.NotSticky;
                }

                RestoreState(restoredState);
            }

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
            CancelPendingHideRetryLocked();
        }

        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    private void StartOrReplaceSession(HiddenAppSessionState session)
    {
        HiddenAppSessionStoreState state;
        bool accepted;
        lock (_sync)
        {
            _storeState = UpdatePersistedState(current =>
                current.ActiveSession is { } reserved
                && string.Equals(reserved.SessionId, session.SessionId, StringComparison.Ordinal)
                    ? current.StartOrReplace(session with { PreviousSession = null }, DateTimeOffset.UtcNow)
                    : current);
            accepted = _storeState.ActiveSession is not null && Matches(_storeState.ActiveSession, session);
            if (!accepted)
            {
                Log.Info(LogTag,
                    $"Ignoring stale hidden-session start for {session.PackageName}, sessionId={session.SessionId}.");
                EnsurePendingHideRetryLocked();
            }
            else
            {
                CancelMonitorLocked();
            }
            state = _storeState;
        }

        StartForegroundServiceNotification(state);
        if (!accepted)
        {
            StopServiceIfIdleOrUpdateNotification(state);
            return;
        }

        lock (_sync)
        {
            if (_storeState.ActiveSession is null || !Matches(_storeState.ActiveSession, session)) return;

            var cancellation = new CancellationTokenSource();
            var cancellationToken = cancellation.Token;
            _monitorCts = cancellation;
            _ = HiddenAppSessionConcurrency.QueueMonitor(
                cancellationToken,
                token => MonitorSessionSafelyAsync(session, token));
            EnsurePendingHideRetryLocked();
        }

        _usageReducer = null;
#if DEBUG
        _lastUsageDiagnosticAt = default;
#endif
        _lastUsageSessionObservation = null;
        _nextUsageEventsQueryBeginUnixTimeMilliseconds = GetSessionStartedAt(session)
            .AddSeconds(-2)
            .ToUnixTimeMilliseconds();
        _usageEventsProblemWarningLogged = false;

        if (!AndroidUsageStatsAccessApi.HasAccess(this, LogTag, false, false))
        {
            var updatedLaunchResult = GetSessionLaunchResult(session)
                .WithIssue(AndroidAppLaunchIssueKind.UsageAccessDenied, "monitor_usage_access=denied");
            updatedLaunchResult.Log(LogTag);
            session = session with { LaunchResult = updatedLaunchResult };
            lock (_sync)
            {
                if (_storeState.ActiveSession is not null && Matches(_storeState.ActiveSession, session))
                {
                    _storeState = UpdatePersistedState(current =>
                        current.ActiveSession is { } active && Matches(active, session)
                            ? current with { ActiveSession = session }
                            : current);
                }
            }
        }

        StartForegroundServiceNotification(_storeState);
        Log.Info(
            LogTag,
            $"Started hidden-session monitor for {session.PackageName}, taskId={session.TaskId}, startedAt={GetSessionStartedAt(session):O}, fastPollMs={FastPollInterval.TotalMilliseconds}, steadyPollMs={SteadyPollInterval.TotalMilliseconds}, hideDelayMs={UserBackgroundHideDelay.TotalMilliseconds}.");
    }

    private void RestoreState(HiddenAppSessionStoreState state)
    {
        lock (_sync)
        {
            _storeState = state;
            CancelMonitorLocked();
            CancelPendingHideRetryLocked();
        }

        StartForegroundServiceNotification(state);
        if (!state.RequiresPackageMonitoring)
        {
            StopServiceIfIdleOrUpdateNotification(state);
            return;
        }

        lock (_sync)
        {
            if (state.ActiveSession is { } activeSession)
            {
                var cancellation = new CancellationTokenSource();
                var cancellationToken = cancellation.Token;
                _monitorCts = cancellation;
                _ = HiddenAppSessionConcurrency.QueueMonitor(
                    cancellationToken,
                    token => MonitorSessionSafelyAsync(activeSession, token));
            }

            EnsurePendingHideRetryLocked();
        }

        Log.Info(
            LogTag,
            $"Restored hidden-session state. active={state.ActiveSession?.PackageName ?? "<none>"}, pendingHides={state.PendingHides.Length}.");
    }

    private async Task MonitorSessionSafelyAsync(HiddenAppSessionState session, CancellationToken cancellationToken)
    {
        try
        {
            await MonitorSessionAsync(session, cancellationToken).ConfigureAwait(false);
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
            var observation = ObserveSession(session, startedAt, now);
            var transition = stateMachine.MoveNext(now, IsDeviceInteractive(), observation);
            Log.Debug(
                LogTag,
                $"package={session.PackageName}; foreground={observation.IsForeground}; inactive={observation.ConfirmedInactive}; delegated={observation.IsSystemDelegatedFlow}; statePhase={transition.Phase}; stateDecision={transition.DecisionReason}; stateAction={transition.Action}.");
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
                Log.Info(
                    LogTag,
                    $"Freeze decision: freeze. package={session.PackageName}, top={observation.TopPackage ?? "<none>"}, inactiveSince={FormatTime(transition.InactiveSince)}, inactiveForMs={transition.InactiveFor?.TotalMilliseconds ?? 0:0}, reason={transition.CompletionReason ?? "<none>"}, decisionReason={transition.DecisionReason}.");
                CompleteSession(session, transition.CompletionReason ?? "state_machine_completed");
                return;
            }

            try
            {
                await Task.Delay(transition.PollDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private SessionObservation ObserveSession(HiddenAppSessionState session, DateTimeOffset startedAt,
        DateTimeOffset now)
    {
        var usageObservation = ObserveUsageEvents(session.PackageName, startedAt, now);
        return new SessionObservation(
            usageObservation?.IsForeground == true,
            usageObservation?.TopPackage,
            usageObservation?.IsForeground == false && usageObservation.ConfirmedInactive,
            usageObservation?.InactiveSince,
            usageObservation?.SawTargetForeground == true,
            usageObservation?.IsSystemDelegatedFlow == true);
    }

    private bool IsDeviceInteractive()
    {
        return AndroidSystemApi.GetPowerManager(this)?.IsInteractive != false;
    }

    private void CompleteSession(HiddenAppSessionState session, string reason)
    {
        HiddenAppSessionStoreState updatedState;
        var completionStarted = false;
        lock (_sync)
        {
            updatedState = UpdatePersistedState(current =>
            {
                if (current.ActiveSession is not { } active || !Matches(active, session)) return current;

                completionStarted = true;
                return (current with { ActiveSession = session })
                    .BeginCompletion(session.SessionId, reason, DateTimeOffset.UtcNow);
            });
            if (!completionStarted) return;

            _storeState = updatedState;
            CancelMonitorLocked();
            EnsurePendingHideRetryLocked();
        }

        StartForegroundServiceNotification(updatedState);
    }

    private void EnsurePendingHideRetryLocked()
    {
        if (_storeState.PendingHides.Length == 0 || _pendingHideRetryCts is not null) return;

        var cancellation = new CancellationTokenSource();
        _pendingHideRetryCts = cancellation;
        _ = Task.Run(() => RetryPendingHidesSafelyAsync(cancellation));
    }

    private async Task RetryPendingHidesSafelyAsync(CancellationTokenSource cancellation)
    {
        var restartRequired = false;
        try
        {
            await RetryPendingHidesAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Pending re-hide loop failed: {exception}");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
                restartRequired = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pendingHideRetryCts, cancellation))
                {
                    _pendingHideRetryCts = null;
                    if (restartRequired) EnsurePendingHideRetryLocked();
                }
            }
        }
    }

    private async Task RetryPendingHidesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HiddenAppPendingHideState[] due;
            TimeSpan delay;
            lock (_sync)
            {
                if (_storeState.PendingHides.Length == 0) return;

                var now = DateTimeOffset.UtcNow;
                due = _storeState.GetDuePendingHides(now);
                delay = due.Length > 0
                    ? TimeSpan.Zero
                    : GetNextPendingHideDelay(_storeState, now);
            }

            if (due.Length == 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var pending in due)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessPendingHide(pending);
            }
        }
    }

    private void ProcessPendingHide(HiddenAppPendingHideState pending)
    {
        using var operation = HiddenAppSessionConcurrency.EnterOperation();
        var pendingWasCanceled = false;
        lock (_sync)
        {
            TryLoadPersistedState(out var persistedState);
            var active = persistedState.ActiveSession;
            if (active is not null
                && string.Equals(active.PackageName, pending.Session.PackageName, StringComparison.Ordinal))
            {
                _storeState = UpdatePersistedState(current => current.StartOrReplace(active, DateTimeOffset.UtcNow));
                return;
            }

            if (!persistedState.PendingHides.Any(item => string.Equals(
                    item.Session.SessionId,
                    pending.Session.SessionId,
                    StringComparison.Ordinal)))
            {
                _storeState = persistedState;
                pendingWasCanceled = true;
            }
        }

        if (pendingWasCanceled)
        {
            StopServiceIfIdleOrUpdateNotification(_storeState);
            return;
        }

        var outcome = TryHidePackage(this, pending, ref _adminComponent);
        HiddenAppSessionStoreState updatedState;
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var stateChanged = false;
            updatedState = UpdatePersistedState(current =>
            {
                var updated = outcome == HiddenAppHideAttemptResult.Failed
                    ? current.RecordHideFailure(pending.Session.SessionId, now)
                    : current.ConfirmHidden(pending.Session.SessionId, now);
                stateChanged = !ReferenceEquals(updated, current);
                return updated;
            });
            if (!stateChanged) return;
            _storeState = updatedState;
        }

        if (outcome == HiddenAppHideAttemptResult.Failed)
        {
            StartForegroundServiceNotification(updatedState);
            return;
        }

        CompleteConfirmedHide(pending);
        StopServiceIfIdleOrUpdateNotification(updatedState);
    }

    private static HiddenAppHideAttemptResult TryHidePackage(
        Context context,
        HiddenAppPendingHideState pending,
        ref ComponentName? admin)
    {
        var session = pending.Session;
        var reason = pending.Reason;
        try
        {
            if (IsPackageMissing(context, session.PackageName))
            {
                Log.Info(LogTag,
                    $"Skipping re-hide because {session.PackageName} is no longer installed. reason={reason}.");
                return HiddenAppHideAttemptResult.NoHideRequired;
            }

            if (AndroidWorkProfilePackageClassifier.IsSystemPackage(context.PackageManager, session.PackageName))
            {
                Log.Info(LogTag,
                    $"Skipping re-hide for system work-profile app {session.PackageName}. reason={reason}.");
                return HiddenAppHideAttemptResult.NoHideRequired;
            }

            if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } policyManager)
            {
                Log.Warn(LogTag, $"DevicePolicyManager unavailable, could not hide {session.PackageName} again.");
                return HiddenAppHideAttemptResult.Failed;
            }

            admin ??= AgnosiaUtilities.GetAdminComponent(context, typeof(AgnosiaDeviceAdminReceiver));
            if (!AndroidPolicyApi.TrySetApplicationHidden(
                    policyManager, admin, session.PackageName, true, LogTag, out _))
            {
                Log.Warn(LogTag, $"Android did not confirm re-hiding {session.PackageName}. reason={reason}");
                return HiddenAppHideAttemptResult.Failed;
            }

            return HiddenAppHideAttemptResult.ConfirmedHidden;
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to hide {session.PackageName} again: {exception}");
            return HiddenAppHideAttemptResult.Failed;
        }
    }

    private static bool IsPackageMissing(Context context, string packageName)
    {
        try
        {
            var application = context.PackageManager?.GetApplicationInfo(
                packageName,
                AndroidSystemApi.GetInstalledApplicationFlags());
            return application is null || (application.Flags & ApplicationInfoFlags.Installed) == 0;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return true;
        }
    }

    private void CompleteConfirmedHide(HiddenAppPendingHideState pending)
    {
        var session = pending.Session;
        var reason = pending.Reason;
        var launchResult = GetSessionLaunchResult(session)
            .WithStage(AndroidAppLaunchStage.PackageRehidden, reason);
        launchResult.Log(LogTag);
        session = session with { LaunchResult = launchResult };
        if (string.Equals(reason, HiddenAppSessionStoreState.SessionReplacedReason, StringComparison.Ordinal))
        {
            Log.Debug(LogTag,
                $"Skipping VPN enable after replacing {session.PackageName}; another hidden-app session is active.");
            return;
        }

        if (string.Equals(reason, ScreenNonInteractiveReason, StringComparison.Ordinal))
        {
            Log.Debug(LogTag,
                $"Screen-lock freeze completed for {session.PackageName}; notifying parent profile so VPN restore is not dependent on the parent lock receiver.");
        }

        if (!TryNotifyParentWithPendingIntent(session, reason))
        {
            Log.Warn(LogTag,
                $"Hidden session {session.SessionId} has no available parent PendingIntent callback.");
            lock (_sync)
            {
                _storeState = UpdatePersistedState(state =>
                    state.RecordParentNotificationFailure(session.SessionId, DateTimeOffset.UtcNow));
            }
        }
    }

    private void StopServiceIfIdleOrUpdateNotification(HiddenAppSessionStoreState state)
    {
        if (state.RequiresPackageMonitoring)
        {
            StartForegroundServiceNotification(state);
            return;
        }

        lock (_sync)
        {
            TryLoadPersistedState(out _storeState);
            if (_storeState.RequiresPackageMonitoring) return;
            CancelPendingHideRetryLocked();
        }

        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    private static TimeSpan GetNextPendingHideDelay(
        HiddenAppSessionStoreState state,
        DateTimeOffset now)
    {
        var nextAttemptAt = state.PendingHides.Min(pending => pending.NextAttemptAtUnixTimeMilliseconds);
        var delay = DateTimeOffset.FromUnixTimeMilliseconds(nextAttemptAt) - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private bool TryNotifyParentWithPendingIntent(HiddenAppSessionState session, string reason)
    {
        if (session.ParentFrozenCallback is not { } callback)
        {
            Log.Debug(LogTag,
                $"No parent pending-intent callback is available for {session.PackageName}.");
            return false;
        }

        try
        {
            Log.Debug(LogTag,
                $"Sending parent pending-intent callback for frozen app {session.PackageName}. reason={reason}");
            if (string.IsNullOrWhiteSpace(session.ParentCallbackLaunchId)) return false;
            WorkVpnRecoveryAlarm.Send(this, session.PackageName, session.ParentCallbackLaunchId, callback);
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
        HiddenAppSessionState session)
    {
        var intent = new Intent(context, typeof(HiddenAppSessionMonitorService));
        intent.SetAction(action);
        intent.PutExtra(ExtraSessionId, session.SessionId);
        intent.PutExtra(ExtraPackageName, session.PackageName);
        intent.PutExtra(ExtraDisplayName, session.DisplayName);
        intent.PutExtra(ExtraTaskId, session.TaskId);
        intent.PutExtra(ExtraStartedAtUnixTimeMilliseconds, session.StartedAtUnixTimeMilliseconds);
        GetSessionLaunchResult(session).WriteToIntent(intent);
        return intent;
    }

    private static bool TryReadSession(Intent? intent, out HiddenAppSessionState session)
    {
        var sessionId = intent?.GetStringExtra(ExtraSessionId);
        var packageName = intent?.GetStringExtra(ExtraPackageName);
        var displayName = intent?.GetStringExtra(ExtraDisplayName);
        var taskId = intent?.GetIntExtra(ExtraTaskId, -1) ?? -1;
        var startedAt = intent?.GetLongExtra(ExtraStartedAtUnixTimeMilliseconds, 0) ?? 0;

        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(packageName)
            || string.IsNullOrWhiteSpace(displayName)
            || taskId < 0)
        {
            session = HiddenAppSessionState.Empty;
            return false;
        }

        var launchResult = AndroidAppLaunchResult.TryRead(intent, out var restoredLaunchResult)
            ? restoredLaunchResult.WithDisplayName(displayName)
            : AndroidAppLaunchResult.CommandReceived(packageName, displayName);
        session = new HiddenAppSessionState(
                sessionId,
                packageName,
                displayName,
                taskId,
                startedAt > 0 ? startedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                launchResult)
            with
            {
                ParentFrozenCallback = AndroidIntentExtras.ReadParentFrozenCallback(intent),
                ParentCallbackLaunchId = AndroidIntentExtras.ReadParentCallbackLaunchId(intent)
            };
        return true;
    }

    private static bool Matches(HiddenAppSessionState left, HiddenAppSessionState right)
    {
        return string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal);
    }

    private static DateTimeOffset GetSessionStartedAt(HiddenAppSessionState session)
    {
        return session.StartedAtUnixTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(session.StartedAtUnixTimeMilliseconds)
            : DateTimeOffset.UtcNow;
    }

    private UsageSessionObservation? ObserveUsageEvents(
        string packageName, DateTimeOffset startedAt, DateTimeOffset now)
    {
        if (!AndroidUsageStatsAccessApi.HasAccess(this, LogTag, false, false))
        {
            WarnUsageEventsProblemOnce("Usage stats access is not granted in the work profile.");
            return null;
        }
        if (AndroidSystemApi.GetUsageStatsManager(this) is not { } manager) return null;
        try
        {
            var begin = Math.Max(Math.Max(startedAt.AddSeconds(-2).ToUnixTimeMilliseconds(),
                now.Subtract(UsageEventsLookback).ToUnixTimeMilliseconds()),
                _nextUsageEventsQueryBeginUnixTimeMilliseconds);
            var events = manager.QueryEvents(begin, now.ToUnixTimeMilliseconds());
            if (events is null) return null;
            var items = ReadUsageEvents(events);
            _usageReducer ??= new HiddenAppUsageObservationReducer(packageName, startedAt);
            var result = _usageReducer.Reduce(items);
            var observation = new UsageSessionObservation(result.IsForeground, result.ConfirmedInactive,
                result.SawTargetForeground, result.InactiveSince, result.TopPackage, result.IsSystemDelegatedFlow);
            if (observation != _lastUsageSessionObservation)
                Log.Debug(LogTag, $"Usage observation changed. package={packageName}, sessionStart={startedAt:O}, foreground={result.IsForeground}, inactive={result.ConfirmedInactive}, inactiveSince={result.InactiveSince:O}, sawTarget={result.SawTargetForeground}, delegated={result.IsSystemDelegatedFlow}.");
#if DEBUG
            // Diagnostic control query never advances the production cursor or changes the observation.
            if (!result.SawTargetForeground && now - startedAt <= TimeSpan.FromMinutes(2)
                && now - _lastUsageDiagnosticAt >= TimeSpan.FromSeconds(5))
            {
                _lastUsageDiagnosticAt = now;
                var fullBegin = startedAt.AddSeconds(-2).ToUnixTimeMilliseconds();
                var full = manager.QueryEvents(fullBegin, now.ToUnixTimeMilliseconds());
                Log.Debug(LogTag, $"Usage control query. user={(global::Android.OS.Process.MyUid() / 100000)}, package={packageName}, session={_storeState.ActiveSession?.SessionId}, launchId={_storeState.ActiveSession?.ParentCallbackLaunchId}, begin={begin}, end={now.ToUnixTimeMilliseconds()}, cursor={_nextUsageEventsQueryBeginUnixTimeMilliseconds}, fullBegin={fullBegin}, normalCount={items.Count}, fullAvailable={full is not null}.");
                if (full is not null)
                    foreach (var item in ReadUsageEvents(full).Where(item => item.PackageName == packageName).TakeLast(32))
                        Log.Debug(LogTag, $"Usage control event. package={item.PackageName}, class={item.ClassName}, type={item.Type}, timestamp={item.Timestamp}, inNormal={items.Contains(item)}.");
            }
#endif
            _lastUsageSessionObservation = observation;
            var latest = items.Count == 0 ? 0 : items.Max(item => item.Timestamp);
            _nextUsageEventsQueryBeginUnixTimeMilliseconds = Math.Max(begin,
                latest > 0 ? latest - 1 : now.AddSeconds(-1).ToUnixTimeMilliseconds());
            return observation;
        }
        catch (Exception exception)
        {
            WarnUsageEventsProblemOnce($"Usage query failed for {packageName}: {exception.Message}");
            return null;
        }
    }

    private static List<HiddenAppUsageEvent> ReadUsageEvents(UsageEvents events)
    {
        var result = new List<HiddenAppUsageEvent>();
        using var item = new UsageEvents.Event();
        while (events.HasNextEvent && events.GetNextEvent(item))
            result.Add(new HiddenAppUsageEvent(item.TimeStamp, (int)item.EventType, item.PackageName, item.ClassName));
        return result;
    }

    private void WarnUsageEventsProblemOnce(string message)
    {
        if (_usageEventsProblemWarningLogged) return;

        _usageEventsProblemWarningLogged = true;
        Log.Warn(LogTag, message);
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

    private void CancelMonitorLocked()
    {
        if (_monitorCts is null) return;

        _monitorCts.Cancel();
        _monitorCts.Dispose();
        _monitorCts = null;
    }

    private void CancelPendingHideRetryLocked()
    {
        if (_pendingHideRetryCts is null) return;

        _pendingHideRetryCts.Cancel();
        _pendingHideRetryCts.Dispose();
        _pendingHideRetryCts = null;
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
            if (_storeState.ActiveSession is null || !Matches(_storeState.ActiveSession, session))
                return updatedSession;

            _storeState = UpdatePersistedState(current =>
                current.ActiveSession is { } active && Matches(active, session)
                    ? current with { ActiveSession = updatedSession }
                    : current);
        }

        return updatedSession;
    }

}

internal enum HiddenAppHideAttemptResult
{
    Failed,
    ConfirmedHidden,
    NoHideRequired
}
