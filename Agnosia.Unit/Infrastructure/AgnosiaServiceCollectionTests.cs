using Agnosia.Infrastructure;
using Agnosia.Android.Commands;
using Agnosia.Android.Infrastructure;
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

    [Fact]
    public void AddAgnosiaAndroidRegistersCommandCenterDependencies()
    {
        using var provider = new ServiceCollection()
            .AddAgnosiaAndroid()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<AndroidCommandScheduler>());
        Assert.NotNull(provider.GetRequiredService<AndroidCommandCenter>());
        Assert.NotNull(provider.GetRequiredService<AndroidCommandHandlerExecutor>());
        var handlerKinds = provider.GetServices<IAndroidCommandHandler>()
            .Select(handler => handler.Kind)
            .ToHashSet();
        AssertContainsAll(
            handlerKinds,
            [
                AndroidCommandKind.ProfilePing,
                AndroidCommandKind.QueryAppIcon,
                AndroidCommandKind.QueryAppIcons,
                AndroidCommandKind.QueryApps,
                AndroidCommandKind.QueryCrossProfilePackages,
                AndroidCommandKind.QueryLogs,
                AndroidCommandKind.QueryPermissions
            ]);
        var transportKinds = provider.GetServices<IAndroidCommandTransport>()
            .Select(transport => transport.Kind)
            .ToHashSet();
        Assert.Contains(AndroidCommandTransportKind.DirectLocal, transportKinds);
        Assert.Contains(AndroidCommandTransportKind.SilentWorkProfile, transportKinds);
    }

    private static void AssertContainsAll<T>(
        HashSet<T> actual,
        IReadOnlyList<T> expected)
        where T : notnull
    {
        foreach (var item in expected)
            Assert.Contains(item, actual);
    }
}
