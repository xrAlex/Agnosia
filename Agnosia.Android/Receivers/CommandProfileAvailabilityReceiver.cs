using Android.Content;

namespace Agnosia.Android.Receivers;

// These protected system broadcasts are delivered to registered receivers, not manifest receivers.
public sealed class CommandProfileAvailabilityReceiver : BroadcastReceiver
{
    public static CommandProfileAvailabilityReceiver Register(Context context)
    {
        var receiver = new CommandProfileAvailabilityReceiver();
        using var filter = new IntentFilter();
        foreach (var action in new[] { Intent.ActionManagedProfileAvailable, Intent.ActionManagedProfileUnavailable,
                     Intent.ActionManagedProfileUnlocked, Intent.ActionManagedProfileRemoved, Intent.ActionManagedProfileAdded })
            filter.AddAction(action);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else
            context.RegisterReceiver(receiver, filter);
        return receiver;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !ProviderTransportOptions.Enabled) return;
        AgnosiaRuntime.Initialize(context);
        ServiceRegistry.GetRequiredService<CommandAccessCoordinator>().NotifyProfileAvailabilityChanged();
    }
}
