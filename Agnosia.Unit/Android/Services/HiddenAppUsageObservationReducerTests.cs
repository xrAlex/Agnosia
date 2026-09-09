using Agnosia.Android.Services;
using Xunit;

namespace Agnosia.Unit.Android.Services;

public sealed class HiddenAppUsageObservationReducerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeMilliseconds(100000);
    private static HiddenAppUsageEvent Event(int seconds, int type, string package = "target", string activity = "Main")
        => new(Start.AddSeconds(seconds).ToUnixTimeMilliseconds(), type, package, activity);

    [Fact]
    public void Fresh_stop_without_resume_confirms_inactivity_and_duplicates_preserve_timestamp()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        var observation = reducer.Reduce([Event(3, 23)]);
        Assert.True(observation.ConfirmedInactive);
        Assert.False(observation.SawTargetForeground);
        Assert.Equal(Start.AddSeconds(3), observation.InactiveSince);
        Assert.Equal(observation, reducer.Reduce([Event(3, 23)]));
        Assert.Equal(observation, reducer.Reduce([]));
    }

    [Fact]
    public void Old_events_foreign_packages_pause_and_unavailable_access_do_not_confirm_inactivity()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        Assert.False(reducer.Reduce([Event(-1, 23), Event(3, 23, "other")]).ConfirmedInactive);
        Assert.False(reducer.Reduce([Event(4, 2)]).ConfirmedInactive);
        reducer.Reduce([Event(5, 23)]);
        Assert.False(reducer.Reduce(null).ConfirmedInactive);
        Assert.False(new HiddenAppUsageObservationReducer("target", Start.AddSeconds(10))
            .Reduce([Event(5, 23)]).ConfirmedInactive);
    }

    [Fact]
    public void Delivery_order_does_not_override_a_newer_resume()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        Assert.True(reducer.Reduce([Event(5, 1), Event(3, 23)]).IsForeground);
        Assert.True(reducer.Reduce([Event(2, 2)]).IsForeground);
        Assert.False(reducer.Reduce([Event(6, 23)]).IsForeground);
    }

    [Fact]
    public void Old_activity_stop_does_not_hide_new_activity()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        var observation = reducer.Reduce([Event(1, 1), Event(2, 1, activity: "Second"), Event(3, 23)]);
        Assert.True(observation.IsForeground);
        Assert.False(observation.ConfirmedInactive);
    }

    [Fact]
    public void Delegated_flow_and_system_dialog_hold_session_until_successor()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        var delegated = reducer.Reduce([Event(1, 1), Event(2, 23), Event(3, 1, "com.android.settings")]);
        Assert.True(delegated.IsSystemDelegatedFlow);
        Assert.False(delegated.ConfirmedInactive);
        var exited = reducer.Reduce([Event(8, 1, "launcher")]);
        Assert.True(exited.ConfirmedInactive);
        Assert.Equal(Start.AddSeconds(2), exited.InactiveSince);
        var dialog = reducer.Reduce([Event(9, 1, "com.android.permissioncontroller")]);
        Assert.False(dialog.ConfirmedInactive);
    }

    [Fact]
    public void Stop_after_delegated_activity_resume_preserves_delegation()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        Assert.True(reducer.Reduce([Event(1, 1), Event(2, 2), Event(3, 1, "com.android.settings")]).IsSystemDelegatedFlow);
        Assert.True(reducer.Reduce([Event(4, 23)]).IsSystemDelegatedFlow);
        Assert.True(reducer.Reduce([Event(5, 1, "launcher")]).ConfirmedInactive);
        var oneQuery = new HiddenAppUsageObservationReducer("target", Start);
        Assert.True(oneQuery.Reduce([Event(1, 1), Event(2, 2), Event(3, 1, "com.android.settings"), Event(4, 23)])
            .IsSystemDelegatedFlow);
    }

    [Fact]
    public void Stops_of_both_activities_confirm_inactivity_in_either_order()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        reducer.Reduce([Event(1, 1), Event(2, 1, activity: "Second")]);
        reducer.Reduce([Event(3, 23, activity: "Second")]);
        Assert.True(reducer.Reduce([Event(4, 23)]).ConfirmedInactive);
        var delayed = new HiddenAppUsageObservationReducer("target", Start);
        delayed.Reduce([Event(3, 23, activity: "Second"), Event(4, 23)]);
        Assert.True(delayed.Reduce([Event(2, 1, activity: "Second")]).ConfirmedInactive);
    }

    [Fact]
    public void Late_pause_preserves_known_system_delegation()
    {
        var reducer = new HiddenAppUsageObservationReducer("target", Start);
        reducer.Reduce([Event(1, 1), Event(3, 1, "com.android.settings"), Event(4, 23)]);
        Assert.True(reducer.Reduce([Event(2, 2)]).IsSystemDelegatedFlow);
    }
}
