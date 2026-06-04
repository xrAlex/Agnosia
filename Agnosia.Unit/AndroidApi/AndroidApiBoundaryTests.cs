using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.AndroidApi;

public sealed class AndroidApiBoundaryTests
{
    private static readonly string[] ForbiddenSourceTokens =
    [
        "IPlatformBridge",
        "ServiceRegistry",
        "LocalStorageManager",
        "SettingsManager",
        "AndroidSettingsStore",
        "AndroidAppLogArchive",
        "AndroidActivityCommandGateway",
        "AndroidProfileCommandGateway",
        "IAndroidActivityHost",
        "AuthenticationUtility",
        "AgnosiaRuntime",
        "AgnosiaUtilities",
        "AppPermissionRiskCatalog",
        "AndroidPackageAccessPolicy"
    ];

    [Fact]
    public void Android_api_project_does_not_contain_service_or_business_logic()
    {
        var projectRoot = RepositoryPaths.Get("Agnosia.Android.Api");
        var sourceFiles = Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var forbiddenFileNames = sourceFiles
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(IsForbiddenServiceOrDomainFileName)
            .ToArray();
        Assert.Empty(forbiddenFileNames);

        var tokenViolations = sourceFiles
            .SelectMany(path => FindForbiddenTokens(projectRoot, path))
            .ToArray();
        Assert.Empty(tokenViolations);
    }

    private static bool IsForbiddenServiceOrDomainFileName(string fileName)
    {
        return fileName.EndsWith("Coordinator", StringComparison.Ordinal)
               || fileName.EndsWith("Manager", StringComparison.Ordinal)
               || fileName.EndsWith("Store", StringComparison.Ordinal)
               || fileName.EndsWith("Reader", StringComparison.Ordinal)
               || fileName is "AndroidPlatformBridge"
                   or "AndroidAppInventoryApi"
                   or "AndroidHiddenShortcutApi"
                   or "AndroidVpnAutomationApi";
    }

    private static IEnumerable<string> FindForbiddenTokens(string projectRoot, string path)
    {
        var source = File.ReadAllText(path);
        var relativePath = Path.GetRelativePath(projectRoot, path);
        foreach (var token in ForbiddenSourceTokens)
            if (source.Contains(token, StringComparison.Ordinal))
                yield return $"{relativePath}: {token}";
    }
}
