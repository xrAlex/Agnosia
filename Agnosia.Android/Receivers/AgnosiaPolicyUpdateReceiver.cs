using Android;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(
    Name = "com.agnosia.app.AgnosiaPolicyUpdateReceiver",
    Permission = Manifest.Permission.BindDeviceAdmin,
    Exported = true)]
[IntentFilter(
[
    ActionDevicePolicySetResult,
    ActionDevicePolicyChanged
])]
public sealed class AgnosiaPolicyUpdateReceiver : BroadcastReceiver
{
    private const string LogTag = "AgnosiaPolicyUpdate";
    private const string ActionDevicePolicySetResult = "android.app.admin.action.DEVICE_POLICY_SET_RESULT";
    private const string ActionDevicePolicyChanged = "android.app.admin.action.DEVICE_POLICY_CHANGED";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null) global::Agnosia.Android.Platform.AgnosiaRuntime.Initialize(context);

        Log.Info(
            LogTag,
            $"Device policy update received. action={intent?.Action ?? "<none>"}, extras={FormatExtras(intent?.Extras)}.");
    }

    private static string FormatExtras(Bundle? extras)
    {
        if (extras is null || extras.Size() == 0) return "<none>";

        var keys = extras.KeySet();
        if (keys is null) return "<none>";

        var entries = new List<string>();
        foreach (var key in keys)
        {
            var value = extras.Get(key);
            entries.Add($"{key}={value?.ToString() ?? "<null>"}");
        }

        return string.Join(", ", entries);
    }
}
