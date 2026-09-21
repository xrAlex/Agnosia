using Agnosia.Android.Commands;
using Agnosia.Android.Commands.Handlers;
using Agnosia.Android.Commands.Transports;
using Agnosia.Android.Vpn;
#if AGNOSIA_ANDROID
using Agnosia.Platform;
#endif
using Microsoft.Extensions.DependencyInjection;

namespace Agnosia.Android.Infrastructure;

internal static class AndroidServiceCollectionExtensions
{
    public static IServiceCollection AddAgnosiaAndroid(this IServiceCollection services)
    {
        services.AddSingleton<AndroidCommandScheduler>();
        services.AddSingleton<AndroidCommandCenter>();
        services.AddSingleton<AndroidCommandHandlerExecutor>();
        services.AddSingleton<IAndroidCommandHandler, ProfilePingCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, QueryAppIconCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, QueryAppIconsCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, QueryAppsCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, QueryCrossProfilePackagesCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, QueryLogsCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, ClearLogsCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, QueryPermissionsCommandHandler>();
        services.AddSingleton<IAndroidCommandHandler, QueryPackageStateCommandHandler>();
#if AGNOSIA_ANDROID
        services.AddSingleton<AndroidCommandExecutionContextFactory>();
        services.AddSingleton<IAndroidCommandTransport, DirectLocalCommandTransport>();
        services.AddSingleton<IAndroidCommandTransport, ActivityCommandTransport>();

        services.AddSingleton<LocalStorageManager>();
        services.AddSingleton(provider =>
        {
            var storage = provider.GetRequiredService<LocalStorageManager>();
            var profileAdapter = new VpnRestoreProfileAdapter(
                () => Activities.VpnRestoreRecoveryActivity.CurrentHost
                    ?? provider.GetRequiredService<IAndroidActivityHostAccessor>().GetRequiredHost());
            return new VpnRestoreOwnershipCoordinator(
                () => storage.GetString(StorageKeys.VpnRestoreOwnershipState),
                raw =>
                {
                    VpnRestoreOwnershipCodec.TryDeserialize(storage.GetString(StorageKeys.VpnRestoreOwnershipState), out var previous);
                    storage.SetStringDurably(StorageKeys.VpnRestoreOwnershipState, raw);
                    if (VpnRestoreOwnershipCodec.TryDeserialize(raw, out var committed)
                        && previous.PendingLaunchDispatched && previous.PendingOwner is { } pending
                        && !committed.PendingLaunchDispatched && !committed.RestoreReady)
                        VpnRestoreRetryScheduler.Cancel(global::Android.App.Application.Context, pending.PackageName, pending.LaunchId);
                    if (VpnRestoreOwnershipCodec.TryDeserialize(raw, out var state)
                        && state.RestoreRequired
                        && (state.RestoreReady || state.PendingLaunchDispatched)
                        && (state.PendingOwner ?? state.ActiveOwner) is { } owner)
                        if (!VpnRestoreRetryScheduler.Schedule(global::Android.App.Application.Context,
                                typeof(Activities.VpnRestoreRecoveryActivity), owner.PackageName, owner.LaunchId,
                                attempt: state.RestoreReady ? 0 : 1))
                            throw new IOException("Android could not schedule VPN restoration.");
                },
                () =>
                {
                    VpnRestoreOwnershipCodec.TryDeserialize(storage.GetString(StorageKeys.VpnRestoreOwnershipState), out var previous);
                    storage.RemoveDurably(StorageKeys.VpnRestoreOwnershipState);
                    var owner = previous.ActiveOwner ?? previous.PendingOwner;
                    if (owner is not null)
                        VpnRestoreRetryScheduler.Cancel(global::Android.App.Application.Context, owner.PackageName, owner.LaunchId);
                },
                () => storage.GetBoolean(StorageKeys.HaveActiveVpnSession),
                () => storage.RemoveDurably(StorageKeys.HaveActiveVpnSession),
                queryPackageState: profileAdapter.QueryAsync,
                confirmRecovery: profileAdapter.ConfirmRecoveryAsync);
        });
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<IAndroidActivityHostAccessor, AndroidActivityHostAccessor>();
        services.AddSingleton(provider => new AndroidActivityCommandGateway(
            provider.GetRequiredService<IAndroidActivityHostAccessor>().GetRequiredHost));
        services.AddSingleton(provider => new AndroidProvisioningCoordinator(
            provider.GetRequiredService<AndroidActivityCommandGateway>(),
            provider.GetRequiredService<IAndroidActivityHostAccessor>().GetRequiredHost));
        services.AddSingleton(provider => new AndroidSettingsCoordinator(
            provider.GetRequiredService<IAndroidActivityHostAccessor>().GetInitializedActivity));
        services.AddSingleton<AndroidDashboardReader>();
        services.AddSingleton(provider => new AndroidPermissionCoordinator(
            provider.GetRequiredService<AndroidActivityCommandGateway>(),
            provider.GetRequiredService<AndroidProvisioningCoordinator>().StartProvisioningAsync));
        services.AddSingleton<AndroidAppCommandCoordinator>();
        services.AddSingleton<AndroidModuleCoordinator>();
        services.AddSingleton(provider => new AndroidPlatformBridge(
            provider.GetRequiredService<IAndroidActivityHostAccessor>(),
            provider.GetRequiredService<AndroidActivityCommandGateway>(),
            provider.GetRequiredService<AndroidDashboardReader>(),
            provider.GetRequiredService<AndroidPermissionCoordinator>(),
            provider.GetRequiredService<AndroidAppCommandCoordinator>(),
            provider.GetRequiredService<AndroidModuleCoordinator>(),
            provider.GetRequiredService<AndroidProvisioningCoordinator>(),
            provider.GetRequiredService<AndroidSettingsCoordinator>()));
        services.AddSingleton<IPlatformBridge>(provider => provider.GetRequiredService<AndroidPlatformBridge>());
#else
        services.AddSingleton<IAndroidCommandTransport, DirectLocalCommandTransport>();
#endif

        return services;
    }
}
