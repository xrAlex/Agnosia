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

using StringBuilder = System.Text.StringBuilder;

namespace Agnosia.Android.Services;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
[Property("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE",
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
    private const string ActionRetryPendingHides = "agnosia.action.RETRY_PENDING_HIDDEN_APP_SESSIONS";
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
    private static readonly TimeSpan SystemDelegatedUsageFallbackWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UsageEventsLookback = TimeSpan.FromMinutes(10);

    private readonly Lock _sync = new();
    private CancellationTokenSource? _monitorCts;
    private CancellationTokenSource? _pendingHideRetryCts;
    private HiddenAppSessionStoreState _storeState = HiddenAppSessionStoreState.Empty;
    private ComponentName? _adminComponent;
    private UsageObservationSnapshot? _lastUsageObservationSnapshot;
    private UsageSessionObservation? _lastUsageSessionObservation;
    private long _nextUsageEventsQueryBeginUnixTimeMilliseconds;
    private bool _usageEventsProblemWarningLogged;

    public static bool StartMonitoring(
        Context context,
        string packageName,
        string displayName,
        int taskId,
        AndroidAppLaunchResult launchResult,
        PendingIntent? parentFrozenCallback = null,
        string? parentCallbackLaunchId = null)
    {
        Log.Info(LogTag, $"StartMonitoring requested for {packageName}, taskId={taskId}.");
        var intent = CreateCommandIntent(context, ActionStart, packageName, displayName, taskId, launchResult);
        if (parentFrozenCallback is not null)
            intent.PutExtra(AndroidCommandContract.ExtraParentFrozenCallback, parentFrozenCallback);
        if (!string.IsNullOrWhiteSpace(parentCallbackLaunchId))
            intent.PutExtra(AndroidCommandContract.ExtraCallbackLaunchId, parentCallbackLaunchId);

        return AndroidServiceApi.TryStartForegroundService(
            context,
            intent,
            LogTag,
            $"Android не смог запустить монитор скрытого приложения {packageName}.");
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
        if (!TryLoadPersistedState(out var state) || state.IsEmpty)
        {
            Log.Info(LogTag, "No persisted hidden-app session to complete on screen lock.");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        state = state.PrepareForScreenLock(now);
        PersistState(state);
        ComponentName? admin = null;
        foreach (var pending in state.PendingHides.ToArray())
        {
            var outcome = TryHidePackage(context, pending, ref admin);
            state = outcome == HiddenAppHideAttemptResult.Failed
                ? state.RecordHideFailure(pending.Session.SessionId, now)
                : state.ConfirmHidden(pending.Session.SessionId, now);
            PersistState(state);
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

    public static void EnsurePendingHideRetryRunning(Context context)
    {
        var intent = new Intent(context, typeof(HiddenAppSessionMonitorService));
        intent.SetAction(ActionRetryPendingHides);
        AndroidServiceApi.TryStartForegroundService(
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
        lock (_sync)
        {
            _storeState = _storeState.StartOrReplace(session, DateTimeOffset.UtcNow);
            PersistState(_storeState);
            CancelMonitorLocked();
            state = _storeState;
        }

        StartForegroundServiceNotification(state);
        lock (_sync)
        {
            if (_storeState.ActiveSession is null || !Matches(_storeState.ActiveSession, session)) return;

            _monitorCts = new CancellationTokenSource();
            _ = Task.Run(() => MonitorSessionSafelyAsync(session, _monitorCts.Token));
            EnsurePendingHideRetryLocked();
        }

        _lastUsageObservationSnapshot = null;
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
                    _storeState = _storeState with { ActiveSession = session };
                    PersistState(_storeState);
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
        lock (_sync)
        {
            if (state.ActiveSession is { } activeSession)
            {
                _monitorCts = new CancellationTokenSource();
                _ = Task.Run(() => MonitorSessionSafelyAsync(activeSession, _monitorCts.Token));
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
        lock (_sync)
        {
            updatedState = _storeState.BeginCompletion(session.SessionId, reason, DateTimeOffset.UtcNow);
            if (ReferenceEquals(updatedState, _storeState)) return;

            _storeState = updatedState;
            PersistState(updatedState);
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
        lock (_sync)
        {
            var active = _storeState.ActiveSession;
            if (active is not null
                && string.Equals(active.PackageName, pending.Session.PackageName, StringComparison.Ordinal))
            {
                _storeState = _storeState.ConfirmHidden(pending.Session.SessionId, DateTimeOffset.UtcNow);
                PersistState(_storeState);
                return;
            }

            if (!_storeState.PendingHides.Any(item => string.Equals(
                    item.Session.SessionId,
                    pending.Session.SessionId,
                    StringComparison.Ordinal)))
            {
                return;
            }
        }

        var outcome = TryHidePackage(this, pending, ref _adminComponent);
        HiddenAppSessionStoreState updatedState;
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            updatedState = outcome == HiddenAppHideAttemptResult.Failed
                ? _storeState.RecordHideFailure(pending.Session.SessionId, now)
                : _storeState.ConfirmHidden(pending.Session.SessionId, now);
            if (ReferenceEquals(updatedState, _storeState)) return;

            _storeState = updatedState;
            PersistState(updatedState);
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
            policyManager.SetApplicationHidden(admin, session.PackageName, true);
            if (!policyManager.IsApplicationHidden(admin, session.PackageName))
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
            Log.Warn(LogTag,
                $"Hidden session {session.SessionId} has no available parent PendingIntent callback.");
    }

    private void StopServiceIfIdleOrUpdateNotification(HiddenAppSessionStoreState state)
    {
        if (!state.IsEmpty)
        {
            StartForegroundServiceNotification(state);
            return;
        }

        lock (_sync)
        {
            if (!_storeState.IsEmpty) return;
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
        session = HiddenAppSessionState.Create(
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
                if (HiddenAppUsageEventPolicy.IsForeground(eventType))
                {
                    foregroundEvents++;
                    latestForegroundPackage = eventPackage;
                    latestForegroundClassName = eventClassName;
                    latestForegroundEventName = HiddenAppUsageEventPolicy.GetName(eventType);
                    latestForegroundAt = usageEvent.TimeStamp;
                }

                if (!string.Equals(eventPackage, packageName, StringComparison.Ordinal)) continue;

                targetEvents++;
                if (HiddenAppUsageEventPolicy.IsForeground(eventType)) sawTargetForeground = true;

                if (!HiddenAppUsageEventPolicy.IsLifecycleTransition(eventType)) continue;
                latestTargetEventType = eventType;
                latestTargetEventAt = usageEvent.TimeStamp;
                latestTargetClassName = eventClassName;
                latestTargetEventName = HiddenAppUsageEventPolicy.GetName(eventType);
                AppendUsageEventTrace(targetUsageEvents, latestTargetEventName, eventClassName, usageEvent.TimeStamp);
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
                    var confirmedInactive = previousDelegatedObservation.InactiveSince is not null;
                    observation = new UsageSessionObservation(
                        false,
                        confirmedInactive,
                        previousDelegatedObservation.SawTargetForeground || sawTargetForeground,
                        previousDelegatedObservation.InactiveSince,
                        latestForegroundPackage,
                        false);
                    reason = confirmedInactive
                        ? "delegated_flow_exited_after_confirmed_target_invisibility"
                        : "delegated_flow_exited_without_confirmed_target_invisibility";
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
            else if (HiddenAppUsageEventPolicy.IsForeground(latestTargetEventType))
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
                var inactiveSince = HiddenAppUsageEventPolicy.IsConfirmedInvisible(latestTargetEventType)
                    && latestTargetEventAt > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(latestTargetEventAt)
                        : (DateTimeOffset?)null;
                observation = new UsageSessionObservation(
                    false,
                    false,
                    hasSeenTargetForeground,
                    inactiveSince,
                    latestForegroundPackage,
                    true);
                reason = "system_delegated_usage_foreground";
            }
            else if (IsTransientSystemPackage(latestForegroundPackage))
            {
                observation =
                    new UsageSessionObservation(true, false, sawTargetForeground, null, latestForegroundPackage, false);
                reason = "transient_system_ui_foreground";
            }
            else if (HiddenAppUsageEventPolicy.IsConfirmedInvisible(latestTargetEventType)
                     && string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal))
            {
                observation =
                    new UsageSessionObservation(false, false, sawTargetForeground, null, latestForegroundPackage, false);
                reason = "target_inactive_but_top_still_target";
            }
            else if (HiddenAppUsageEventPolicy.IsConfirmedInvisible(latestTargetEventType)
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
            else if (HiddenAppUsageEventPolicy.IsConfirmedInvisible(latestTargetEventType))
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
                reason = "target_paused_visibility_unconfirmed";
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

        if (latestForegroundAt < previousObservation.InactiveSince.Value.ToUnixTimeMilliseconds()
            || string.IsNullOrWhiteSpace(latestForegroundPackage)
            || string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal)
            || IsTransientSystemPackage(latestForegroundPackage)) return false;
        
        observation = previousObservation with
        {
            ConfirmedInactive = true,
            TopPackage = latestForegroundPackage
        };
        
        reason = "target_inactive_then_successor_foreground";
        return true;

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
            || !HiddenAppUsageEventPolicy.CanStartDelegatedFlow(latestTargetEventType)
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

            _storeState = _storeState with { ActiveSession = updatedSession };
            PersistState(_storeState);
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
