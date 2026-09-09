using Agnosia.Services;
using Xunit;

namespace Agnosia.Unit.Services;

public sealed class HiddenAppSessionMonitorStateMachineTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 5, 21, 10, 0, 0, TimeSpan.Zero);

    // Проверяет авто-скрытие после foreground-сессии и выдержанной задержки inactive.
    [Fact]
    public void MoveNext_completes_after_user_used_hidden_app_and_background_delay_elapsed()
    {
        var stateMachine = CreateStateMachine();

        var foreground = stateMachine.MoveNext(
            StartedAt.AddSeconds(1),
            true,
            TargetForeground());
        Assert.Equal(HiddenAppSessionTransitionAction.KeepAlive, foreground.Action);
        Assert.Equal(HiddenAppSessionMonitorPhase.TargetForegroundOrDelegated, foreground.Phase);
        Assert.True(foreground.TargetForegroundFirstSeen);

        var inactiveSince = StartedAt.AddSeconds(3);
        var pending = stateMachine.MoveNext(
            inactiveSince,
            true,
            ConfirmedInactive(inactiveSince));
        Assert.Equal(HiddenAppSessionTransitionAction.KeepAlive, pending.Action);
        Assert.Equal(HiddenAppSessionMonitorPhase.InactiveCandidate, pending.Phase);
        Assert.Equal("confirmed_inactivity_delay_pending", pending.DecisionReason);

        var completed = stateMachine.MoveNext(
            inactiveSince.AddSeconds(10),
            true,
            ConfirmedInactive(inactiveSince));

        Assert.Equal(HiddenAppSessionTransitionAction.Complete, completed.Action);
        Assert.Equal(HiddenAppSessionMonitorPhase.Completed, completed.Phase);
        Assert.Equal(HiddenAppSessionCompletionKind.ConfirmedInvisible, completed.CompletionKind);
        Assert.Equal("user_backgrounded_or_closed", completed.CompletionReason);
        Assert.Equal("confirmed_inactivity_delay_elapsed", completed.DecisionReason);
        Assert.Equal(TimeSpan.FromSeconds(10), completed.InactiveFor);
    }

    [Fact]
    public void MoveNext_completes_from_confirmed_current_session_stop_without_resume()
    {
        var stateMachine = CreateStateMachine();
        var inactiveSince = StartedAt.AddSeconds(1);

        var pending = stateMachine.MoveNext(inactiveSince, true,
            ConfirmedInactive(inactiveSince, sawTargetForeground: false));
        Assert.Equal(HiddenAppSessionMonitorPhase.InactiveCandidate, pending.Phase);
        var transition = stateMachine.MoveNext(
            inactiveSince.AddSeconds(10),
            true,
            ConfirmedInactive(inactiveSince, sawTargetForeground: false));

        Assert.Equal(HiddenAppSessionTransitionAction.Complete, transition.Action);
        Assert.False(transition.TargetForegroundFirstSeen);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(null)]
    public void MoveNext_rejects_inactivity_without_current_session_timestamp(int? seconds)
    {
        var observation = new SessionObservation(false, null, true,
            seconds is { } value ? StartedAt.AddSeconds(value) : null, false, false);
        var result = CreateStateMachine().MoveNext(StartedAt.AddMinutes(1), true, observation);
        Assert.Equal(HiddenAppSessionTransitionAction.KeepAlive, result.Action);
    }

    // Проверяет сброс таймера авто-скрытия при возврате пользователя в target-приложение.
    [Fact]
    public void MoveNext_resets_pending_hide_when_target_returns_to_foreground()
    {
        var stateMachine = CreateStateMachine();
        stateMachine.MoveNext(StartedAt.AddSeconds(1), true, TargetForeground());
        var firstInactiveSince = StartedAt.AddSeconds(3);
        stateMachine.MoveNext(firstInactiveSince, true, ConfirmedInactive(firstInactiveSince));

        var foregroundAgain = stateMachine.MoveNext(
            StartedAt.AddSeconds(4),
            true,
            TargetForeground());

        Assert.Equal(HiddenAppSessionTransitionAction.KeepAlive, foregroundAgain.Action);
        Assert.Equal(HiddenAppSessionMonitorPhase.TargetForegroundOrDelegated, foregroundAgain.Phase);
        Assert.Equal(firstInactiveSince, foregroundAgain.ResetInactiveSince);

        var secondInactiveSince = StartedAt.AddSeconds(5);
        var pending = stateMachine.MoveNext(
            firstInactiveSince.AddSeconds(10),
            true,
            ConfirmedInactive(secondInactiveSince));

        Assert.Equal(HiddenAppSessionTransitionAction.KeepAlive, pending.Action);
        Assert.Equal(HiddenAppSessionMonitorPhase.InactiveCandidate, pending.Phase);
        Assert.Equal("confirmed_inactivity_delay_pending", pending.DecisionReason);
        Assert.Equal(TimeSpan.FromSeconds(8), pending.InactiveFor);
    }

    // Проверяет, что системный экран, открытый из target-приложения, продлевает сессию.
    [Fact]
    public void MoveNext_keeps_alive_for_system_delegated_flow_after_target_inactivity()
    {
        var stateMachine = CreateStateMachine();
        stateMachine.MoveNext(StartedAt.AddSeconds(1), true, TargetForeground());
        var inactiveSince = StartedAt.AddSeconds(2);
        stateMachine.MoveNext(inactiveSince.AddSeconds(9), true, ConfirmedInactive(inactiveSince));

        var delegated = stateMachine.MoveNext(
            inactiveSince.AddSeconds(10),
            true,
            SystemDelegatedFlow());

        Assert.Equal(HiddenAppSessionTransitionAction.KeepAlive, delegated.Action);
        Assert.Equal(HiddenAppSessionMonitorPhase.TargetForegroundOrDelegated, delegated.Phase);
        Assert.Equal("system_delegated_flow", delegated.DecisionReason);
        Assert.Equal(inactiveSince, delegated.ResetInactiveSince);
        Assert.Null(delegated.InactiveSince);
    }

    private static HiddenAppSessionMonitorStateMachine CreateStateMachine()
    {
        return new HiddenAppSessionMonitorStateMachine(
            StartedAt,
            TimeSpan.FromSeconds(45),
            TimeSpan.FromSeconds(45),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromSeconds(3));
    }

    private static SessionObservation TargetForeground()
    {
        return new SessionObservation(
            true,
            "com.example.hidden",
            false,
            null,
            true,
            false);
    }

    private static SessionObservation ConfirmedInactive(
        DateTimeOffset inactiveSince,
        bool sawTargetForeground = true)
    {
        return new SessionObservation(
            false,
            "com.example.launcher",
            true,
            inactiveSince,
            sawTargetForeground,
            false);
    }

    private static SessionObservation SystemDelegatedFlow()
    {
        return new SessionObservation(
            false,
            "com.android.settings",
            false,
            null,
            true,
            true);
    }
}
