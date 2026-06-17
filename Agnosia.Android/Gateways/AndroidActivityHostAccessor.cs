using Android.App;

namespace Agnosia.Android.Gateways;

internal sealed class AndroidActivityHostAccessor : IAndroidActivityHostAccessor
{
    private WeakReference<IAndroidActivityHost>? _activityHostReference;

    public void Attach(IAndroidActivityHost activityHost)
    {
        ArgumentNullException.ThrowIfNull(activityHost);

        AgnosiaRuntime.Initialize(activityHost.CurrentActivity);
        _activityHostReference = new WeakReference<IAndroidActivityHost>(activityHost);
    }

    public void Detach()
    {
        _activityHostReference = null;
    }

    public IAndroidActivityHost GetRequiredHost()
    {
        return _activityHostReference?.TryGetTarget(out var activityHost) == true
            ? activityHost
            : throw new InvalidOperationException("Agnosia is not attached to an active Android activity.");
    }

    public Activity GetInitializedActivity()
    {
        var activity = GetRequiredHost().CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return activity;
    }
}
