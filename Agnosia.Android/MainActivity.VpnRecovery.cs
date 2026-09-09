using Agnosia.Android.Vpn;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android;

public partial class MainActivity
{
    private bool _vpnRecoveryRunning;
    private DateTimeOffset _lastVpnRecovery;

    private async Task RecoverVpnOnResumeAsync()
    {
        if (_vpnRecoveryRunning || DateTimeOffset.UtcNow - _lastVpnRecovery < TimeSpan.FromSeconds(30)) return;
        _vpnRecoveryRunning = true;
        _lastVpnRecovery = DateTimeOffset.UtcNow;
        try
        {
            if (AgnosiaUtilities.IsProfileOwner(this)) return;
            var coordinator = ServiceRegistry.GetRequiredService<VpnRestoreOwnershipCoordinator>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var recovery = await coordinator.RecoverAsync(
                () => WorkAppFrozenHandler.RestoreOwnedVpnAsync(this, "primary_activity_resumed"), timeout.Token);
            if (recovery.RestoreSucceeded) WorkAppFrozenHandler.HideOverlay(this, LogTag);
            if (!recovery.Result.Succeeded) Log.Warn(LogTag, recovery.Result.Message);
        }
        catch (Exception exception) { Log.Warn(LogTag, $"VPN recovery remains pending: {exception.Message}"); }
        finally { _vpnRecoveryRunning = false; }
    }
}
