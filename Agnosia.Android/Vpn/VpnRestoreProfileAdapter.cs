using System.Text.Json;
using Agnosia.Models;
using Agnosia.Android.Commands;
using Agnosia.Android.Commands.Transports;
using Agnosia.Android.Gateways;

namespace Agnosia.Android.Vpn;

internal sealed class VpnRestoreProfileAdapter(Func<IAndroidActivityHost> getActivityHost)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public Task<VpnRestorePackageStateResult> QueryAsync(
        VpnRestoreOwner owner,
        CancellationToken cancellationToken)
    {
        return QueryCoreAsync(owner, null, cancellationToken);
    }

    public async Task<OperationResult> ConfirmRecoveryAsync(
        VpnRestoreOwner owner,
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(owner, owner.LaunchId, cancellationToken)
            .ConfigureAwait(false);
        if (!snapshot.Result.Succeeded) return snapshot.Result;
        return snapshot.RecoveryAcknowledged
            ? OperationResult.Success("Рабочий профиль подтвердил завершение восстановления VPN.")
            : OperationResult.Failure("Рабочий профиль не подтвердил завершение восстановления VPN.");
    }

    private async Task<VpnRestorePackageStateResult> QueryCoreAsync(
        VpnRestoreOwner owner,
        string? confirmedLaunchId,
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(owner, confirmedLaunchId, cancellationToken)
            .ConfigureAwait(false);
        return snapshot.Result.Succeeded
            ? VpnRestorePackageStateResult.Success(
                snapshot.Installed,
                snapshot.Hidden,
                snapshot.LaunchId)
            : VpnRestorePackageStateResult.Failure(snapshot.Result.Message);
    }

    private async Task<PackageStateSnapshot> ReadSnapshotAsync(
        VpnRestoreOwner owner,
        string? confirmedLaunchId,
        CancellationToken cancellationToken)
    {
        var envelope = new AndroidCommandEnvelope(
            Guid.NewGuid(),
            AndroidCommandKind.QueryPackageState,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.NonInteractive,
            AndroidCommandPriority.Background,
            CommandTimeout,
            JsonSerializer.Serialize(new PackageStateQuery(owner.PackageName, confirmedLaunchId)));
        var gateway = new AndroidActivityCommandGateway(getActivityHost);
        var transport = new ActivityCommandTransport(gateway);
        var result = await transport.ExecuteAsync(envelope, cancellationToken).ConfigureAwait(false);
        return PackageStateResultInterpreter.ReadSnapshot(result, owner.PackageName);
    }
}
