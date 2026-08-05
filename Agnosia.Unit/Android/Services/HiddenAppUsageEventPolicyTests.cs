using Agnosia.Android.Services;
using Xunit;

namespace Agnosia.Unit.Android.Services;

public sealed class HiddenAppUsageEventPolicyTests
{
    // Проверяет, что foreground подтверждает только ACTIVITY_RESUMED.
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(23, false)]
    [InlineData(24, false)]
    public void IsForeground_only_accepts_activity_resumed(int eventType, bool expected)
    {
        Assert.Equal(expected, HiddenAppUsageEventPolicy.IsForeground(eventType));
    }

    // Проверяет multi-window правило: PAUSED ещё видим, STOPPED/DESTROYED уже нет.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(23, true)]
    [InlineData(24, true)]
    public void IsConfirmedInvisible_rejects_paused_but_accepts_stopped_and_destroyed(
        int eventType,
        bool expected)
    {
        Assert.Equal(expected, HiddenAppUsageEventPolicy.IsConfirmedInvisible(eventType));
    }

    // Проверяет сохранение delegated-flow переходов после любого background lifecycle event.
    [Theory]
    [InlineData(2)]
    [InlineData(23)]
    [InlineData(24)]
    public void CanStartDelegatedFlow_accepts_any_target_background_transition(int eventType)
    {
        Assert.True(HiddenAppUsageEventPolicy.CanStartDelegatedFlow(eventType));
    }
}
