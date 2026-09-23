using Agnosia.Android.Infrastructure;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

// Root may address a non-exported component. Ordinary apps must never bootstrap an authentication key.
[BroadcastReceiver(Name = DirectProfileProvisioningCommands.SetupReceiver, Exported = false)]
public sealed class DirectProfileSetupReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != DirectProfileProvisioningCommands.SetupAction) return;
        var pending = GoAsync();
        if (pending is null) return;
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(() =>
        {
            try
            {
                AgnosiaRuntime.Initialize(appContext);
                var existing = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetString(StorageKeys.AuthKey);
                var key = intent.GetStringExtra(DirectProfileProvisioningCommands.ExtraKey);
                var expectedUser = intent.GetIntExtra(DirectProfileProvisioningCommands.ExtraUser, -1);
                var managed = AndroidSystemApi.GetUserManager(appContext)?.IsManagedProfile == true;
                if (!DirectProfileSetupAuthentication.CanAccept(existing, key, managed,
                        AgnosiaUtilities.IsProfileOwner(appContext), global::Android.OS.Process.MyUserHandle()?.GetHashCode() ?? -1, expectedUser))
                    return;

                // Commit before acknowledging. A replay can finish policies after process death without rotating the key.
                if (existing is null)
                    ServiceRegistry.GetRequiredService<LocalStorageManager>().SetStringDurably(StorageKeys.AuthKey, key!);
                AndroidStartup.EnforceWorkProfilePolicies(appContext, true);
                pending.ResultCode = Result.Ok;
                Log.Info("AgnosiaDirectSetup", "Direct work profile setup completed.");
            }
            catch (Exception exception)
            {
                Log.Warn("AgnosiaDirectSetup", $"Direct work profile setup failed: {exception.GetType().Name}.");
            }
            finally
            {
                pending.Finish();
                pending.Dispose();
            }
        });
    }
}
