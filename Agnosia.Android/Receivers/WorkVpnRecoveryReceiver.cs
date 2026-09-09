using Agnosia.Android.Services;
using Agnosia.Android.Infrastructure;
using Agnosia.Android.Vpn;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(Name = "com.agnosia.app.WorkVpnRecoveryReceiver", Exported = false)]
public sealed class WorkVpnRecoveryReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var package = intent.GetStringExtra(WorkVpnRecoveryAlarm.ExtraPackageName);
        var launchId = intent.GetStringExtra(WorkVpnRecoveryAlarm.ExtraLaunchId);
        var callback = AndroidIntentExtras.ReadParentFrozenCallback(intent);
        if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(launchId) || callback is null) return;
        var pending = GoAsync();
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(async () =>
        {
            try
            {
                AgnosiaRuntime.Initialize(appContext);
                if (!AgnosiaUtilities.IsProfileOwner(appContext)) return;
                if (!HiddenAppSessionMonitorService.IsKnownLaunch(package, launchId)) return;

                // Retain the nested callback before any await, query or delivery can fail.
                var attempt = Math.Clamp(intent.GetIntExtra(WorkVpnRecoveryAlarm.ExtraAttempt, 0), 0, 3) + 1;
                if (!WorkVpnRecoveryAlarm.Schedule(appContext, typeof(WorkVpnRecoveryReceiver),
                        package, launchId, callback, attempt))
                    throw new InvalidOperationException("Could not retain VPN callback retry.");

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                using var operation = await HiddenAppSessionConcurrency.EnterOperationAsync(timeout.Token);
                if (!HiddenAppSessionMonitorService.IsKnownLaunch(package, launchId)) return;
                if (!IsPackageHiddenOrMissing(appContext, package)) return;
                HiddenAppSessionMonitorService.PrepareParentNotification(package, launchId);
                WorkVpnRecoveryAlarm.Send(appContext, package, launchId, callback);
            }
            catch (Exception exception)
            {
                Log.Warn("AgnosiaWorkVpnRecovery", $"VPN callback remains pending: {exception.Message}");
            }
            finally { pending?.Finish(); }
        });
    }

    private static bool IsPackageHiddenOrMissing(Context context, string package)
    {
        var manager = context.PackageManager
                      ?? throw new InvalidOperationException("PackageManager is unavailable.");
        try
        {
            var app = manager.GetApplicationInfo(package, AndroidSystemApi.GetInstalledApplicationFlags());
            if (app is null || (app.Flags & ApplicationInfoFlags.Installed) == 0) return true;
            var policy = AndroidSystemApi.GetDevicePolicyManager(context);
            var admin = AgnosiaUtilities.GetAdminComponent(context, typeof(AgnosiaDeviceAdminReceiver));
            return policy?.IsApplicationHidden(admin, package) == true;
        }
        catch (PackageManager.NameNotFoundException) { return true; }
    }
}
