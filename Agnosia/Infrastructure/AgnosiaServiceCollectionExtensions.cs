using Agnosia.Platform;
using Agnosia.Services;
using Agnosia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agnosia.Infrastructure;

public static class AgnosiaServiceCollectionExtensions
{
    public static IServiceCollection AddAgnosiaCore(this IServiceCollection services)
    {
        services.TryAddSingleton<AppStartupState>();
        services.TryAddSingleton<UnsupportedPlatformBridge>();
        services.TryAddSingleton<IPlatformBridge>(provider => provider.GetRequiredService<UnsupportedPlatformBridge>());
        services.TryAddSingleton<IDashboardPlatformService>(provider => provider.GetRequiredService<IPlatformBridge>());
        services.TryAddSingleton<IPlatformEventLogReader>(provider => provider.GetRequiredService<IPlatformBridge>());
        services.TryAddSingleton<IPermissionPlatformService>(provider => provider.GetRequiredService<IPlatformBridge>());
        services.TryAddSingleton<IOnboardingPlatformService>(provider => provider.GetRequiredService<IPlatformBridge>());
        services.TryAddSingleton<IAppCommandService>(provider => provider.GetRequiredService<IPlatformBridge>());
        services.TryAddSingleton<ISettingsPlatformService>(provider => provider.GetRequiredService<IPlatformBridge>());
        services.TryAddSingleton<IModulePlatformService>(provider => provider.GetRequiredService<IPlatformBridge>());
        services.TryAddTransient<IAppEventLogService, BoundedAppEventLogService>();
        services.TryAddTransient(provider => new DashboardWorkspaceViewModel(
            provider.GetRequiredService<IDashboardPlatformService>(),
            provider.GetRequiredService<IPlatformEventLogReader>(),
            provider.GetRequiredService<IPermissionPlatformService>(),
            provider.GetRequiredService<IOnboardingPlatformService>(),
            provider.GetRequiredService<IAppCommandService>(),
            provider.GetRequiredService<ISettingsPlatformService>(),
            provider.GetRequiredService<IModulePlatformService>(),
            provider.GetRequiredService<IAppEventLogService>()));

        return services;
    }
}
