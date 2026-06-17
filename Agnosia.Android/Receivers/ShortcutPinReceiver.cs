using Android.Content;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(Exported = false)]
public sealed class ShortcutPinReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !string.Equals(intent?.Action, AgnosiaActions.ShortcutPinned, StringComparison.Ordinal))
            return;

        AgnosiaRuntime.Initialize(context);

        var packageName = intent?.GetStringExtra("packageName");
        if (string.IsNullOrWhiteSpace(packageName))
            return;

        HiddenAppShortcutManager.HandlePinnedShortcutConfirmation(context, packageName);
    }
}