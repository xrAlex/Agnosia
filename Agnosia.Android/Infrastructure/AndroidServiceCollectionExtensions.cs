using Agnosia.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Agnosia.Android.Infrastructure;

internal static class AndroidServiceCollectionExtensions
{
    public static IServiceCollection AddAgnosiaAndroid(this IServiceCollection services)
    {
        services.AddSingleton<LocalStorageManager>();
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

        return services;
    }
}
