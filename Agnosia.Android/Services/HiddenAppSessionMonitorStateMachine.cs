namespace Agnosia.Android.Services;

internal sealed class HiddenAppSessionMonitorStateMachine
{
    private readonly DateTimeOffset _startedAt;
    private readonly TimeSpan _initialLaunchGracePeriod;
    private readonly TimeSpan _postLaunchTransientUiGracePeriod;
    private readonly TimeSpan _userBackgroundHideDelay;
    private readonly TimeSpan _initialFastPollingWindow;
    private readonly TimeSpan _fastPollInterval;
    private readonly TimeSpan _steadyPollInterval;
    private readonly TimeSpan _idlePollInterval;

    private DateTimeOffset _lastForegroundAt;
    private DateTimeOffset? _inactiveSince;
    private HiddenAppSessionMonitorPhase _phase = HiddenAppSessionMonitorPhase.WaitingForTargetForeground;
    private bool _hasSeenTarget;
    private bool _launchObservationWarningRaised;

    public HiddenAppSessionMonitorStateMachine(
        DateTimeOffset startedAt,
        TimeSpan initialLaunchGracePeriod,
        TimeSpan postLaunchTransientUiGracePeriod,
        TimeSpan userBackgroundHideDelay,
        TimeSpan initialFastPollingWindow,
        TimeSpan fastPollInterval,
        TimeSpan steadyPollInterval,
        TimeSpan idlePollInterval)
    {
        _startedAt = startedAt;
        _lastForegroundAt = startedAt;
        _initialLaunchGracePeriod = initialLaunchGracePeriod;
        _postLaunchTransientUiGracePeriod = postLaunchTransientUiGracePeriod;
        _userBackgroundHideDelay = userBackgroundHideDelay;
        _initialFastPollingWindow = initialFastPollingWindow;
        _fastPollInterval = fastPollInterval;
        _steadyPollInterval = steadyPollInterval;
        _idlePollInterval = idlePollInterval;
    }

    public HiddenAppSessionTransition MoveNext(
        DateTimeOffset now,
        bool deviceInteractive,
        SessionObservation observation)
    {
        if (!deviceInteractive)
        {
            _phase = HiddenAppSessionMonitorPhase.Completed;
            return Complete(
                HiddenAppSessionCompletionKind.Immediate,
                HiddenAppSessionMonitorService.ScreenNonInteractiveReason,
                "device_non_interactive",
                observation,
                now);
        }

        var targetForegroundFirstSeen = observation.SawTargetForeground && !_hasSeenTarget;
        if (observation.SawTargetForeground)
            _hasSeenTarget = true;

        if (observation.IsSystemDelegatedFlow)
        {
            var previousInactiveSince = ResetInactiveCandidate();
            _hasSeenTarget = true;
            _phase = HiddenAppSessionMonitorPhase.TargetForegroundOrDelegated;
            _lastForegroundAt = now;
            return KeepAlive(
                observation,
                now,
                targetForegroundFirstSeen,
                previousInactiveSince,
                "system_delegated_flow");
        }

        if (observation.IsForeground)
        {
            var previousInactiveSince = ResetInactiveCandidate();
            _hasSeenTarget = true;
            _phase = HiddenAppSessionMonitorPhase.TargetForegroundOrDelegated;
            _lastForegroundAt = now;
            return KeepAlive(
                observation,
                now,
                targetForegroundFirstSeen,
                previousInactiveSince,
                "target_foreground");
        }

        if (_hasSeenTarget && observation.ConfirmedInactive)
        {
            _inactiveSince ??= observation.InactiveSince ?? now;
            _phase = HiddenAppSessionMonitorPhase.InactiveCandidate;
            var inactiveFor = now - _inactiveSince.Value;
            if (inactiveFor >= _userBackgroundHideDelay)
            {
                _phase = HiddenAppSessionMonitorPhase.Completed;
                return Complete(
                    HiddenAppSessionCompletionKind.AfterTargetTaskRecheck,
                    "user_backgrounded_or_closed",
                    "confirmed_inactivity_delay_elapsed",
                    observation,
                    now,
                    targetForegroundFirstSeen);
            }

            return KeepAlive(
                observation,
                now,
                targetForegroundFirstSeen,
                null,
                "confirmed_inactivity_delay_pending");
        }

        if (!_hasSeenTarget)
        {
            _phase = HiddenAppSessionMonitorPhase.WaitingForTargetForeground;
            var shouldRaiseLaunchWarning = !_launchObservationWarningRaised
                                           && now - _startedAt >= _initialLaunchGracePeriod;
            if (shouldRaiseLaunchWarning)
                _launchObservationWarningRaised = true;

            return KeepAlive(
                observation,
                now,
                targetForegroundFirstSeen,
                null,
                "waiting_for_target_foreground",
                shouldRaiseLaunchWarning);
        }

        var resetInactiveSince = ResetInactiveCandidate();
        _phase = HiddenAppSessionMonitorPhase.TargetForegroundOrDelegated;
        var shouldRaiseTransientWarning = now - _lastForegroundAt >= _postLaunchTransientUiGracePeriod;
        if (shouldRaiseTransientWarning)
            _lastForegroundAt = now;

        return KeepAlive(
            observation,
            now,
            targetForegroundFirstSeen,
            resetInactiveSince,
            "inactivity_not_confirmed",
            false,
            shouldRaiseTransientWarning);
    }

    public void PostponeCompletionBecauseTargetTaskStillPresent(DateTimeOffset now)
    {
        _inactiveSince = null;
        _hasSeenTarget = true;
        _phase = HiddenAppSessionMonitorPhase.TargetForegroundOrDelegated;
        _lastForegroundAt = now;
    }

    private DateTimeOffset? ResetInactiveCandidate()
    {
        var previousInactiveSince = _inactiveSince;
        _inactiveSince = null;
        return previousInactiveSince;
    }

    private HiddenAppSessionTransition KeepAlive(
        SessionObservation observation,
        DateTimeOffset now,
        bool targetForegroundFirstSeen,
        DateTimeOffset? resetInactiveSince,
        string reason,
        bool shouldRaiseLaunchObservationWarning = false,
        bool shouldRaiseTransientUiWarning = false)
    {
        return new HiddenAppSessionTransition(
            HiddenAppSessionTransitionAction.KeepAlive,
            _phase,
            null,
            HiddenAppSessionCompletionKind.None,
            reason,
            targetForegroundFirstSeen,
            resetInactiveSince,
            shouldRaiseLaunchObservationWarning,
            shouldRaiseTransientUiWarning,
            _inactiveSince,
            _inactiveSince is null ? null : now - _inactiveSince.Value,
            GetNextPollDelay(now, observation));
    }

    private HiddenAppSessionTransition Complete(
        HiddenAppSessionCompletionKind completionKind,
        string completionReason,
        string decisionReason,
        SessionObservation observation,
        DateTimeOffset now,
        bool targetForegroundFirstSeen = false)
    {
        return new HiddenAppSessionTransition(
            HiddenAppSessionTransitionAction.Complete,
            _phase,
            completionReason,
            completionKind,
            decisionReason,
            targetForegroundFirstSeen,
            null,
            false,
            false,
            _inactiveSince,
            _inactiveSince is null ? null : now - _inactiveSince.Value,
            GetNextPollDelay(now, observation));
    }

    private TimeSpan GetNextPollDelay(DateTimeOffset now, SessionObservation observation)
    {
        if (_inactiveSince is not null
            || !_hasSeenTarget && now - _startedAt <= _initialFastPollingWindow)
        {
            return _fastPollInterval;
        }

        if (observation.IsSystemDelegatedFlow || observation.IsForeground)
            return _steadyPollInterval;

        return _hasSeenTarget ? _steadyPollInterval : _idlePollInterval;
    }
}

internal enum HiddenAppSessionTransitionAction
{
    KeepAlive,
    Complete
}

internal enum HiddenAppSessionMonitorPhase
{
    WaitingForTargetForeground,
    TargetForegroundOrDelegated,
    InactiveCandidate,
    Completed
}

internal enum HiddenAppSessionCompletionKind
{
    None,
    Immediate,
    AfterTargetTaskRecheck
}

internal sealed record HiddenAppSessionTransition(
    HiddenAppSessionTransitionAction Action,
    HiddenAppSessionMonitorPhase Phase,
    string? CompletionReason,
    HiddenAppSessionCompletionKind CompletionKind,
    string DecisionReason,
    bool TargetForegroundFirstSeen,
    DateTimeOffset? ResetInactiveSince,
    bool ShouldRaiseLaunchObservationWarning,
    bool ShouldRaiseTransientUiWarning,
    DateTimeOffset? InactiveSince,
    TimeSpan? InactiveFor,
    TimeSpan PollDelay);

internal sealed record SessionObservation(
    bool IsForeground,
    string? TopPackage,
    bool ConfirmedInactive,
    DateTimeOffset? InactiveSince,
    bool SawTargetForeground,
    bool IsSystemDelegatedFlow);
