using Agnosia.Infrastructure;
using Agnosia.Platform;
using Agnosia.Services;
using Agnosia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agnosia.Unit.Infrastructure;

public sealed class AgnosiaServiceCollectionTests
{
    [Fact]
    public void AddAgnosiaCoreRegistersWorkspaceDependencies()
    {
        using var provider = new ServiceCollection()
            .AddAgnosiaCore()
            .BuildServiceProvider();

        var workspace = provider.GetRequiredService<DashboardWorkspaceViewModel>();

        Assert.NotNull(workspace);
        Assert.IsType<BoundedAppEventLogService>(provider.GetRequiredService<IAppEventLogService>());
    }

    [Fact]
    public void AddAgnosiaCoreMapsPlatformInterfacesToSameBridge()
    {
        using var provider = new ServiceCollection()
            .AddAgnosiaCore()
            .BuildServiceProvider();

        var bridge = provider.GetRequiredService<IPlatformBridge>();

        Assert.IsType<UnsupportedPlatformBridge>(bridge);
        Assert.Same(bridge, provider.GetRequiredService<IDashboardPlatformService>());
        Assert.Same(bridge, provider.GetRequiredService<IPlatformEventLogReader>());
        Assert.Same(bridge, provider.GetRequiredService<IPermissionPlatformService>());
        Assert.Same(bridge, provider.GetRequiredService<IOnboardingPlatformService>());
        Assert.Same(bridge, provider.GetRequiredService<IAppCommandService>());
        Assert.Same(bridge, provider.GetRequiredService<ISettingsPlatformService>());
        Assert.Same(bridge, provider.GetRequiredService<IModulePlatformService>());
    }
}
