using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using _Microsoft.Android.Resource.Designer;
using Agnosia.Android.Api;
using Agnosia.Android.Receivers;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.Lang;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.AgnosiaLog;
using Math = System.Math;
using OperationCanceledException = System.OperationCanceledException;
using StringBuilder = System.Text.StringBuilder;

namespace Agnosia.Android.Services;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
[MetaData("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE", Value = "monitor_hidden_work_profile_app_until_user_leaves_it")]
public sealed class HiddenAppSessionMonitorService : Service
{
    private const string LogTag = "AgnosiaHiddenSession";
    private const string PermissionControllerPackage = "com.google.android.permissioncontroller";
    private const string AospPermissionControllerPackage = "com.android.permissioncontroller";
    private const string GooglePlayServicesPackage = "com.google.android.gms";
    private const string ActionStart = "agnosia.action.START_HIDDEN_APP_SESSION";
    private const string ActionCompleteForScreenLock = "agnosia.action.COMPLETE_HIDDEN_APP_SESSION_FOR_SCREEN_LOCK";
    private const string ExtraPackageName = "packageName";
    private const string ExtraDisplayName = "displayName";
    private const string ExtraTaskId = "taskId";
    private const string ExtraStartedAtUnixTimeMilliseconds = "startedAtUnixTimeMilliseconds";
    private const string ScreenLockServiceReason = "screen_lock_service";
    private const string ScreenNonInteractiveReason = "screen_non_interactive";
    private const string SessionReplacedReason = "session_replaced";
    private const int NotificationId = 0x57C31;
    private const string NotificationChannelId = "agnosia.hidden-app-session";
    private const string NotificationChannelName = "Сессии Agnosia";
    private const string NotificationChannelDescription = "Мониторинг скрытых приложений в рабочем профиле";
    private const int UsageEventMoveToForegroundOrActivityResumed = 1;
    private const int UsageEventMoveToBackgroundOrActivityPaused = 2;
    private const int UsageEventActivityStopped = 23;
    private const int UsageEventActivityDestroyed = 24;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan InitialLaunchGracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PostLaunchTransientUiGracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan UserBackgroundHideDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UsageEventsLookback = TimeSpan.FromMinutes(10);

    private readonly Lock _sync = new();
    private CancellationTokenSource? _monitorCts;
    private HiddenAppSessionState? _activeSession;
    private ComponentName? _adminComponent;
    private UsageObservationSnapshot? _lastUsageObservationSnapshot;
    private bool _usageEventsProblemWarningLogged;

    public static void StartMonitoring(
        Context context,
        string packageName,
        string displayName,
        int taskId,
        PendingIntent? parentFrozenCallback = null)
    {
        Log.Info(LogTag, $"StartMonitoring requested for {packageName}, taskId={taskId}.");
        var intent = CreateCommandIntent(context, ActionStart, packageName, displayName, taskId);
        if (parentFrozenCallback is not null)
        {
            intent.PutExtra(AndroidCommandContract.ExtraParentFrozenCallback, parentFrozenCallback);
        }

        AndroidServiceApi.TryStartForegroundService(
            context,
            intent,
            LogTag,
            $"Android не смог запустить монитор скрытого приложения {packageName}.");
    }

    public static void RequestScreenLockCompletion(Context context)
    {
        Log.Info(LogTag, "Screen-lock session completion requested.");
        var intent = new Intent(context, typeof(HiddenAppSessionMonitorService));
        intent.SetAction(ActionCompleteForScreenLock);
        AndroidServiceApi.TryStartService(
            context,
            intent,
            LogTag,
            "Android не смог отправить команду завершения сессии при блокировке экрана.");
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AgnosiaRuntime.Initialize(this);
        _adminComponent = AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
        Log.Info(LogTag, "HiddenAppSessionMonitorService created.");
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Log.Info(LogTag, $"OnStartCommand action={intent?.Action ?? "<null>"} startId={startId}.");
        try
        {
            var action = intent?.Action;
            if (string.Equals(action, ActionCompleteForScreenLock, StringComparison.Ordinal))
            {
                CompleteActiveSession(ScreenLockServiceReason);
                StopSelf(startId);
                return StartCommandResult.NotSticky;
            }

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

    public override IBinder? OnBind(Intent? intent) => null;

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
        _usageEventsProblemWarningLogged = false;

        if (previousSession is not null
            && !string.Equals(previousSession.PackageName, session.PackageName, StringComparison.Ordinal))
        {
            Log.Debug(
                LogTag,
                $"Replacing active hidden-session. previous={previousSession.PackageName}, next={session.PackageName}, previousTaskId={previousSession.TaskId}, nextTaskId={session.TaskId}.");
            TryHidePackage(previousSession, SessionReplacedReason);
        }

        StartForegroundServiceNotification(session);
        Log.Info(
            LogTag,
            $"Started hidden-session monitor for {session.PackageName}, taskId={session.TaskId}, startedAt={GetSessionStartedAt(session):O}, pollMs={PollInterval.TotalMilliseconds}, hideDelayMs={UserBackgroundHideDelay.TotalMilliseconds}.");
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
        var lastForegroundAt = startedAt;
        DateTimeOffset? inactiveSince = null;
        var hasSeenTarget = false;
        var launchObservationWarningLogged = false;
        Log.Debug(
            LogTag,
            $"Monitor loop initialized. package={session.PackageName}, taskId={session.TaskId}, startedAt={startedAt:O}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (!IsDeviceInteractive())
            {
                Log.Info(LogTag, $"Freeze decision: freeze immediately because device is non-interactive. package={session.PackageName}, now={now:O}.");
                CompleteSession(session, ScreenNonInteractiveReason);
                return;
            }

            var observation = ObserveSession(session, startedAt, now);
            if (observation.SawTargetForeground)
            {
                if (!hasSeenTarget)
                {
                    Log.Debug(LogTag, $"Target foreground evidence observed. package={session.PackageName}, now={now:O}, top={observation.TopPackage ?? "<none>"}.");
                }

                hasSeenTarget = true;
            }

            if (observation.IsForeground)
            {
                if (inactiveSince is not null)
                {
                    Log.Debug(LogTag, $"Inactive timer reset because target is foreground again. package={session.PackageName}, previousInactiveSince={inactiveSince:O}, now={now:O}.");
                }

                hasSeenTarget = true;
                lastForegroundAt = now;
                inactiveSince = null;
            }
            else if (hasSeenTarget && observation.ConfirmedInactive)
            {
                inactiveSince ??= observation.InactiveSince ?? now;
                var inactiveFor = now - inactiveSince.Value;
                if (inactiveFor >= UserBackgroundHideDelay)
                {
                    Log.Info(
                        LogTag,
                        $"Freeze decision: freeze after confirmed inactivity. package={session.PackageName}, top={observation.TopPackage ?? "<none>"}, inactiveSince={inactiveSince:O}, inactiveForMs={inactiveFor.TotalMilliseconds:0}, hideDelayMs={UserBackgroundHideDelay.TotalMilliseconds:0}.");
                    CompleteSession(session, "user_backgrounded_or_closed");
                    return;
                }
            }
            else if (!hasSeenTarget)
            {
                if (!launchObservationWarningLogged && now - startedAt >= InitialLaunchGracePeriod)
                {
                    launchObservationWarningLogged = true;
                    Log.Warn(
                        LogTag,
                        $"Session {session.PackageName} has not produced foreground evidence yet; keeping it visible instead of hiding on an unconfirmed timeout.");
                }
            }
            else
            {
                if (inactiveSince is not null)
                {
                    Log.Debug(LogTag, $"Inactive timer reset because inactivity is not confirmed. package={session.PackageName}, previousInactiveSince={inactiveSince:O}, now={now:O}, top={observation.TopPackage ?? "<none>"}.");
                }

                inactiveSince = null;
                if (now - lastForegroundAt >= PostLaunchTransientUiGracePeriod)
                {
                    Log.Warn(
                        LogTag,
                        $"Session {session.PackageName} has no current foreground evidence, but inactivity is not confirmed; keeping it visible.");
                    lastForegroundAt = now;
                }
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private SessionObservation ObserveSession(HiddenAppSessionState session, DateTimeOffset startedAt, DateTimeOffset now)
    {
        var usageObservation = ObserveUsageEvents(session.PackageName, startedAt, now);

        var observation = new SessionObservation(
            usageObservation?.IsForeground == true,
            usageObservation?.TopPackage,
            usageObservation?.IsForeground == false && usageObservation.ConfirmedInactive,
            usageObservation?.InactiveSince,
            usageObservation?.SawTargetForeground == true);
        return observation;
    }

    private bool IsDeviceInteractive() =>
        AndroidSystemApi.GetPowerManager(this)?.IsInteractive != false;

    private void CompleteActiveSession(string reason)
    {
        HiddenAppSessionState? session;
        lock (_sync)
        {
            session = _activeSession;
            if (session is null && TryLoadPersistedSession(out var persistedSession))
            {
                session = persistedSession;
                _activeSession = persistedSession;
            }
        }

        if (session is null)
        {
            Log.Info(LogTag, $"No active hidden-app session to complete. reason={reason}");
            return;
        }

        CompleteSession(session, reason);
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

        if (!shouldComplete)
        {
            return;
        }

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

            var admin = _adminComponent ??= AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
            var hiddenApplied = policyManager.SetApplicationHidden(admin, session.PackageName, true);
            if (!hiddenApplied && !policyManager.IsApplicationHidden(admin, session.PackageName))
            {
                Log.Warn(LogTag, $"Android did not confirm re-hiding {session.PackageName}. reason={reason}");
                return;
            }

            Log.Info(LogTag, $"App {session.PackageName} hidden again. reason={reason}");
            if (string.Equals(reason, SessionReplacedReason, StringComparison.Ordinal))
            {
                Log.Info(LogTag, $"Skipping VPN enable after replacing {session.PackageName}; another hidden-app session is active.");
                return;
            }

            if (string.Equals(reason, ScreenLockServiceReason, StringComparison.Ordinal)
                || string.Equals(reason, ScreenNonInteractiveReason, StringComparison.Ordinal))
            {
                Log.Info(LogTag, $"Skipping session-level VPN enable after screen-lock freeze for {session.PackageName}; lock service handles it.");
                return;
            }

            if (TryNotifyParentWithPendingIntent(session, reason))
            {
                return;
            }

            Log.Info(LogTag, $"Notifying parent profile about frozen app {session.PackageName}. reason={reason}");
            var result = AndroidProfileCommandGateway.NotifyParentWorkAppFrozen(
                this,
                $"session_hide:{reason}:{session.PackageName}");
            if (!result.Succeeded)
            {
                Log.Warn(LogTag, $"Could not notify parent profile about frozen app {session.PackageName}: {result.Message}");
                return;
            }

            Log.Info(LogTag, $"Parent profile notification accepted for frozen app {session.PackageName}. reason={reason}");
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
            Log.Debug(LogTag, $"No parent pending-intent callback is available for {session.PackageName}; falling back to cross-profile activity.");
            return false;
        }

        try
        {
            Log.Info(LogTag, $"Sending parent pending-intent callback for frozen app {session.PackageName}. reason={reason}");
            callback.Send(
                this,
                Result.Ok,
                null,
                null,
                null,
                null,
                AndroidPendingIntentApi.CreateSenderBackgroundActivityStartOptions());
            Log.Info(LogTag, $"Parent pending-intent callback sent for frozen app {session.PackageName}. reason={reason}");
            return true;
        }
        catch (PendingIntent.CanceledException exception)
        {
            Log.Warn(LogTag, $"Parent pending-intent callback was canceled for {session.PackageName}: {exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Parent pending-intent callback failed for {session.PackageName}: {exception.Message}");
            return false;
        }
    }

    private static Intent CreateCommandIntent(Context context, string action, string packageName, string displayName, int taskId)
    {
        var intent = new Intent(context, typeof(HiddenAppSessionMonitorService));
        intent.SetAction(action);
        intent.PutExtra(ExtraPackageName, packageName);
        intent.PutExtra(ExtraDisplayName, displayName);
        intent.PutExtra(ExtraTaskId, taskId);
        intent.PutExtra(ExtraStartedAtUnixTimeMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

        session = new HiddenAppSessionState(
            packageName,
            displayName,
            taskId,
            startedAt > 0 ? startedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            ParentFrozenCallback = ReadParentFrozenCallback(intent)
        };
        return true;
    }

    private static PendingIntent? ReadParentFrozenCallback(Intent? intent)
    {
        if (intent is null)
        {
            return null;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return intent.GetParcelableExtra(
                AndroidCommandContract.ExtraParentFrozenCallback,
                Class.FromType(typeof(PendingIntent))) as PendingIntent;
        }

#pragma warning disable CA1422
        return intent.GetParcelableExtra(AndroidCommandContract.ExtraParentFrozenCallback) as PendingIntent;
#pragma warning restore CA1422
    }

    private static bool Matches(HiddenAppSessionState left, HiddenAppSessionState right) =>
        string.Equals(left.PackageName, right.PackageName, StringComparison.Ordinal)
        && left.TaskId == right.TaskId;

    private static DateTimeOffset GetSessionStartedAt(HiddenAppSessionState session) =>
        session.StartedAtUnixTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(session.StartedAtUnixTimeMilliseconds)
            : DateTimeOffset.UtcNow;

    private UsageSessionObservation? ObserveUsageEvents(
        string packageName,
        DateTimeOffset startedAt,
        DateTimeOffset now)
    {
        if (AndroidSystemApi.GetUsageStatsManager(this) is not { } usageStatsManager)
        {
            WarnUsageEventsProblemOnce("UsageStatsManager unavailable; no inactive evidence was produced.");
            return null;
        }

        try
        {
            var begin = Math.Max(
                startedAt.AddSeconds(-2).ToUnixTimeMilliseconds(),
                now.Subtract(UsageEventsLookback).ToUnixTimeMilliseconds());
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
            string? latestTargetEventName = null;
            string? latestForegroundEventName = null;
            var latestForegroundAt = 0L;
            var targetUsageEvents = new StringBuilder();

            while (events.HasNextEvent)
            {
                if (!events.GetNextEvent(usageEvent))
                {
                    break;
                }

                scannedEvents++;
                var eventType = (int)usageEvent.EventType;
                var eventPackage = usageEvent.PackageName;
                if (IsUsageForegroundEvent(eventType))
                {
                    foregroundEvents++;
                    latestForegroundPackage = eventPackage;
                    latestForegroundEventName = GetUsageEventName(eventType);
                    latestForegroundAt = usageEvent.TimeStamp;
                }

                if (!string.Equals(eventPackage, packageName, StringComparison.Ordinal))
                {
                    continue;
                }

                targetEvents++;
                if (IsUsageForegroundEvent(eventType))
                {
                    sawTargetForeground = true;
                }

                if (IsUsageForegroundEvent(eventType) || IsUsageInactiveEvent(eventType))
                {
                    latestTargetEventType = eventType;
                    latestTargetEventAt = usageEvent.TimeStamp;
                    latestTargetEventName = GetUsageEventName(eventType);
                    AppendUsageEventTrace(targetUsageEvents, latestTargetEventName, usageEvent.TimeStamp);
                }
            }

            UsageSessionObservation observation;
            string reason;
            if (latestTargetEventType < 0)
            {
                observation = new UsageSessionObservation(false, false, sawTargetForeground, null, latestForegroundPackage);
                reason = "no_target_lifecycle_event";
            }
            else if (IsUsageForegroundEvent(latestTargetEventType))
            {
                observation = new UsageSessionObservation(true, false, true, null, packageName);
                reason = "target_latest_event_foreground";
            }
            else if (string.Equals(latestForegroundPackage, packageName, StringComparison.Ordinal)
                && latestForegroundAt >= latestTargetEventAt)
            {
                observation = new UsageSessionObservation(true, false, true, null, packageName);
                reason = "target_is_latest_foreground";
            }
            else if (IsTransientSystemPackage(latestForegroundPackage))
            {
                observation = new UsageSessionObservation(true, false, sawTargetForeground, null, latestForegroundPackage);
                reason = "transient_system_ui_foreground";
            }
            else if (IsUsageInactiveEvent(latestTargetEventType))
            {
                observation = new UsageSessionObservation(
                    false,
                    true,
                    sawTargetForeground,
                    latestTargetEventAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(latestTargetEventAt) : null,
                    latestForegroundPackage);
                reason = "target_latest_event_inactive";
            }
            else
            {
                observation = new UsageSessionObservation(false, false, sawTargetForeground, null, latestForegroundPackage);
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
                latestForegroundPackage,
                latestForegroundEventName,
                latestForegroundAt,
                targetUsageEvents.ToString(),
                observation,
                reason);
            return observation;
        }
        catch (Exception)
        {
            WarnUsageEventsProblemOnce($"Usage events query failed for {packageName}; no inactive evidence was produced.");
            return null;
        }
    }

    private void WarnUsageEventsProblemOnce(string message)
    {
        if (_usageEventsProblemWarningLogged)
        {
            return;
        }

        _usageEventsProblemWarningLogged = true;
        Log.Warn(LogTag, message);
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
        string? latestForegroundPackage,
        string? latestForegroundEventName,
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
            observation.InactiveSince,
            latestTargetEventName,
            latestTargetEventAt,
            latestForegroundPackage,
            latestForegroundEventName,
            latestForegroundAt,
            targetUsageEvents,
            scannedEvents,
            targetEvents,
            foregroundEvents,
            reason);
        if (snapshot.Equals(_lastUsageObservationSnapshot))
        {
            return;
        }

        _lastUsageObservationSnapshot = snapshot;
        Log.Debug(
            LogTag,
            $"Usage observation changed. package={packageName}, reason={reason}, queryBegin={DateTimeOffset.FromUnixTimeMilliseconds(queryBegin):O}, queryEnd={queryEnd:O}, scanned={scannedEvents}, targetEvents={targetEvents}, foregroundEvents={foregroundEvents}, targetUsageEvents=[{FormatTrace(targetUsageEvents)}], latestTarget={latestTargetEventName ?? "<none>"}@{FormatUnixTime(latestTargetEventAt)}, latestForeground={latestForegroundPackage ?? "<none>"}:{latestForegroundEventName ?? "<none>"}@{FormatUnixTime(latestForegroundAt)}, resultForeground={observation.IsForeground}, resultInactive={observation.ConfirmedInactive}, sawTargetForeground={observation.SawTargetForeground}, inactiveSince={FormatTime(observation.InactiveSince)}, top={observation.TopPackage ?? "<none>"}.");
    }

    private static bool IsUsageForegroundEvent(int eventType) =>
        eventType == UsageEventMoveToForegroundOrActivityResumed;

    private static bool IsUsageInactiveEvent(int eventType) =>
        eventType is UsageEventMoveToBackgroundOrActivityPaused or UsageEventActivityStopped or UsageEventActivityDestroyed;

    private static string GetUsageEventName(int eventType) =>
        eventType switch
        {
            UsageEventMoveToForegroundOrActivityResumed => "FOREGROUND_OR_RESUMED",
            UsageEventMoveToBackgroundOrActivityPaused => "BACKGROUND_OR_PAUSED",
            UsageEventActivityStopped => "ACTIVITY_STOPPED",
            UsageEventActivityDestroyed => "ACTIVITY_DESTROYED",
            _ => eventType.ToString(CultureInfo.InvariantCulture)
        };

    private static bool IsTransientSystemPackage(string? packageName) =>
        string.Equals(packageName, PermissionControllerPackage, StringComparison.Ordinal)
        || string.Equals(packageName, AospPermissionControllerPackage, StringComparison.Ordinal)
        || string.Equals(packageName, GooglePlayServicesPackage, StringComparison.Ordinal);

    private static string FormatTime(DateTimeOffset? value) =>
        value is null ? "<none>" : value.Value.ToString("O");

    private static string FormatUnixTime(long unixTimeMilliseconds) =>
        unixTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToString("O")
            : "<none>";

    private static void AppendUsageEventTrace(StringBuilder builder, string eventName, long unixTimeMilliseconds)
    {
        if (builder.Length > 0)
        {
            builder.Append(", ");
        }

        builder
            .Append(eventName)
            .Append('@')
            .Append(FormatUnixTime(unixTimeMilliseconds));
    }

    private static string FormatTrace(string value) =>
        string.IsNullOrWhiteSpace(value) ? "<none>" : value;

    private static void PersistSession(HiddenAppSessionState? session)
    {
        if (session is null)
        {
            LocalStorageManager.Instance.Remove(StorageKeys.HiddenAppActiveSession);
            return;
        }

        LocalStorageManager.Instance.SetString(
            StorageKeys.HiddenAppActiveSession,
            JsonSerializer.Serialize(session));
    }

    private static bool TryLoadPersistedSession(out HiddenAppSessionState session)
    {
        var raw = LocalStorageManager.Instance.GetString(StorageKeys.HiddenAppActiveSession);
        if (string.IsNullOrWhiteSpace(raw))
        {
            session = HiddenAppSessionState.Empty;
            return false;
        }

        try
        {
            session = JsonSerializer.Deserialize<HiddenAppSessionState>(raw) ?? HiddenAppSessionState.Empty;
            return !string.IsNullOrWhiteSpace(session.PackageName) && session.TaskId >= 0;
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to restore hidden-app session: {exception.Message}");
            LocalStorageManager.Instance.Remove(StorageKeys.HiddenAppActiveSession);
            session = HiddenAppSessionState.Empty;
            return false;
        }
    }

    private void CancelMonitorLocked()
    {
        if (_monitorCts is null)
        {
            return;
        }

        _monitorCts.Cancel();
        _monitorCts.Dispose();
        _monitorCts = null;
    }

    private void StartForegroundServiceNotification(HiddenAppSessionState session)
    {
        var notification = AndroidNotificationApi.BuildNotification(
            this,
            NotificationChannelId,
            NotificationChannelName,
            NotificationChannelDescription,
            $"Открыто: {session.DisplayName}",
            "Приложение снова скроется через 30 секунд после сворачивания или закрытия.",
            ResourceConstant.Drawable.icon);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
            return;
        }

        StartForeground(NotificationId, notification);
    }

    private sealed record HiddenAppSessionState(
        string PackageName,
        string DisplayName,
        int TaskId,
        long StartedAtUnixTimeMilliseconds = 0)
    {
        public static HiddenAppSessionState Empty { get; } = new(string.Empty, string.Empty, -1);

        [JsonIgnore]
        public PendingIntent? ParentFrozenCallback { get; init; }
    }

    private sealed record SessionObservation(
        bool IsForeground,
        string? TopPackage,
        bool ConfirmedInactive,
        DateTimeOffset? InactiveSince,
        bool SawTargetForeground);

    private sealed record UsageSessionObservation(
        bool IsForeground,
        bool ConfirmedInactive,
        bool SawTargetForeground,
        DateTimeOffset? InactiveSince,
        string? TopPackage);

    private sealed record UsageObservationSnapshot(
        bool IsForeground,
        bool ConfirmedInactive,
        bool SawTargetForeground,
        string? TopPackage,
        DateTimeOffset? InactiveSince,
        string? LatestTargetEventName,
        long LatestTargetEventAt,
        string? LatestForegroundPackage,
        string? LatestForegroundEventName,
        long LatestForegroundAt,
        string TargetUsageEvents,
        int ScannedEvents,
        int TargetEvents,
        int ForegroundEvents,
        string Reason);
}
