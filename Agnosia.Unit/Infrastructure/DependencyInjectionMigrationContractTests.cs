using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Infrastructure;

public sealed class DependencyInjectionMigrationContractTests
{
    private static readonly string[] ForbiddenServiceLocatorPatterns =
    [
        "AndroidPlatformBridge.Instance",
        "LocalStorageManager.Instance",
        "SettingsManager.Instance",
        "UnsupportedPlatformBridge.Instance",
        "ServiceRegistry.PlatformBridge",
        "ServiceRegistry.InitialTheme",
        "ServiceRegistry.SuppressPrimaryUiStartup"
    ];

    [Fact]
    public void ProductionCodeDoesNotUseLegacySingletonAccessors()
    {
        var productionFiles = Directory
            .EnumerateFiles(RepositoryPaths.Root, "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .ToArray();

        var violations = new List<string>();
        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in ForbiddenServiceLocatorPatterns)
            {
                if (text.Contains(pattern, StringComparison.Ordinal))
                    violations.Add($"{Path.GetRelativePath(RepositoryPaths.Root, file)}: {pattern}");
            }
        }

        Assert.Empty(violations);
    }

    private static bool IsProductionSource(string path)
    {
        var relativePath = Path.GetRelativePath(RepositoryPaths.Root, path);
        return relativePath.StartsWith("Agnosia" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               || relativePath.StartsWith("Agnosia.Android" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
