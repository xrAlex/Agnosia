using Microsoft.Extensions.DependencyInjection;

namespace Agnosia.Infrastructure;

public static class ServiceRegistry
{
    private static IServiceProvider _services = CreateDefaultServices();

    public static event Action? PrimaryActivityResumed;

    public static IServiceProvider Services => _services;

    public static void ConfigureServices(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public static T GetRequiredService<T>()
        where T : notnull
    {
        return _services.GetRequiredService<T>();
    }

    public static void NotifyPrimaryActivityResumed()
    {
        PrimaryActivityResumed?.Invoke();
    }

    private static IServiceProvider CreateDefaultServices()
    {
        return new ServiceCollection()
            .AddAgnosiaCore()
            .BuildServiceProvider();
    }
}
