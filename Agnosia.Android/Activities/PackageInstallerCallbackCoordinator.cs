using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

internal static class PackageInstallerCallbackCoordinator
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<string, DummyActivity> Operations = new(StringComparer.Ordinal);

    public static void Register(string operationId, DummyActivity activity)
    {
        lock (Sync)
        {
            Operations.Add(operationId, activity);
        }
    }

    public static void Unregister(string? operationId, DummyActivity activity)
    {
        if (operationId is null) return;
        lock (Sync)
        {
            if (Operations.TryGetValue(operationId, out var owner) && ReferenceEquals(owner, activity))
                Operations.Remove(operationId);
        }
    }

    public static void Dispatch(Intent intent)
    {
        var operationId = intent.GetStringExtra(AndroidCommandContract.ExtraPackageInstallerOperationId);
        if (operationId is null) return;
        DummyActivity? activity;
        lock (Sync)
        {
            if (!Operations.TryGetValue(operationId, out activity))
            {
                Log.Warn("AgnosiaPkgCallback", $"No activity owns installer callback. operationId={operationId}.");
                return;
            }
        }

        activity.RunOnUiThread(() => activity.HandlePackageInstallerCallback(new Intent(intent)));
    }
}
