using Android.Content;

namespace Agnosia.Android.Activities;

internal static class PackageInstallerCallbackCoordinator
{
    private static readonly Lock Sync = new();
    private static DummyActivity? _activeActivity;
    private static Intent? _pendingCallback;

    public static void RegisterActive(DummyActivity activity)
    {
        lock (Sync)
        {
            _activeActivity = activity;
        }
    }

    public static void UnregisterActive(DummyActivity activity)
    {
        lock (Sync)
        {
            if (ReferenceEquals(_activeActivity, activity)) _activeActivity = null;
        }
    }

    public static Intent? TakePendingCallback()
    {
        lock (Sync)
        {
            if (_pendingCallback is null) return null;

            var pendingCallback = new Intent(_pendingCallback);
            _pendingCallback = null;
            return pendingCallback;
        }
    }

    public static void Dispatch(Intent intent)
    {
        DummyActivity? activity;
        lock (Sync)
        {
            activity = _activeActivity;
            if (activity is null)
            {
                _pendingCallback = new Intent(intent);
                return;
            }
        }

        activity.RunOnUiThread(() => activity.HandlePackageInstallerCallback(new Intent(intent)));
    }
}
