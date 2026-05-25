using System.Text.RegularExpressions;
using System.Xml.Linq;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Android;

public sealed class AndroidManifestContractTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    // Проверяет manifest requirements для managed profile и device admin сценариев.
    [Fact]
    public void Manifest_declares_managed_profile_device_admin_requirements()
    {
        var manifest = ReadManifest();
        var root = manifest.Root ?? throw new InvalidOperationException("Android manifest has no root element.");
        var features = root.Elements("uses-feature").ToArray();
        var permissions = root.Elements("uses-permission")
            .Select(element => AndroidAttribute(element, "name"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal("internalOnly", AndroidAttribute(root, "installLocation"));
        AssertRequiredFeature(features, "android.software.device_admin");
        AssertRequiredFeature(features, "android.software.managed_users");

        AssertContainsAll(
            permissions,
            [
                "android.permission.FOREGROUND_SERVICE",
                "android.permission.FOREGROUND_SERVICE_SPECIAL_USE",
                "android.permission.FOREGROUND_SERVICE_SYSTEM_EXEMPTED",
                "android.permission.PACKAGE_USAGE_STATS",
                "android.permission.RECEIVE_BOOT_COMPLETED",
                "android.permission.REQUEST_INSTALL_PACKAGES",
                "android.permission.REQUEST_DELETE_PACKAGES",
                "android.permission.POST_NOTIFICATIONS",
                "android.permission.SYSTEM_ALERT_WINDOW"
            ]);
    }

    // Проверяет, что launcher alias ведет на MainActivity и объявлен как launcher entry point.
    [Fact]
    public void Launcher_alias_targets_main_activity_contract()
    {
        var manifest = ReadManifest();
        var mainActivitySource = ReadMainActivitySource();
        var mainActivityName = ReadMainActivityName(mainActivitySource);
        var launcherActivityName = ReadLauncherActivityName(mainActivitySource);
        var application = manifest.Root?.Element("application")
                          ?? throw new InvalidOperationException("Android manifest has no application element.");
        var alias = application.Elements("activity-alias")
            .Single(element => string.Equals(
                AndroidAttribute(element, "name"),
                launcherActivityName,
                StringComparison.Ordinal));
        var intentFilter = alias.Element("intent-filter")
                           ?? throw new InvalidOperationException("Launcher alias has no intent filter.");
        var actions = intentFilter.Elements("action")
            .Select(element => AndroidAttribute(element, "name"))
            .ToHashSet(StringComparer.Ordinal);
        var categories = intentFilter.Elements("category")
            .Select(element => AndroidAttribute(element, "name"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(mainActivityName, AndroidAttribute(alias, "targetActivity"));
        Assert.Equal("true", AndroidAttribute(alias, "enabled"));
        Assert.Equal("true", AndroidAttribute(alias, "exported"));
        Assert.Contains("android.intent.action.MAIN", actions);
        Assert.Contains("android.intent.category.LAUNCHER", categories);
    }

    // Проверяет, что specialUse FGS subtype генерируется как service-level <property>, а не <meta-data>.
    [Fact]
    public void Hidden_session_monitor_declares_special_use_subtype_as_service_property()
    {
        var source = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Services", "HiddenAppSessionMonitorService.cs"));

        Assert.Contains("ForegroundServiceType = ForegroundService.TypeSpecialUse", source, StringComparison.Ordinal);
        Assert.Contains("[Property(\"android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[MetaData(\"android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE\"", source,
            StringComparison.Ordinal);
    }

    private static XDocument ReadManifest()
    {
        return XDocument.Load(RepositoryPaths.Get("Agnosia.Android", "Properties", "AndroidManifest.xml"));
    }

    private static string ReadMainActivitySource()
    {
        return File.ReadAllText(RepositoryPaths.Get("Agnosia.Android", "MainActivity.cs"));
    }

    private static string ReadMainActivityName(string source)
    {
        return MatchRequired(source, @"\[Activity\([\s\S]*?Name\s*=\s*""(?<value>[^""]+)""");
    }

    private static string ReadLauncherActivityName(string source)
    {
        return MatchRequired(
            source,
            @"public\s+const\s+string\s+LauncherActivityName\s*=\s*""(?<value>[^""]+)""");
    }

    private static string MatchRequired(string source, string pattern)
    {
        var match = Regex.Match(source, pattern);
        if (!match.Success) throw new InvalidOperationException($"Pattern not found: {pattern}");

        return match.Groups["value"].Value;
    }

    private static string AndroidAttribute(XElement element, string name)
    {
        return element.Attribute(Android + name)?.Value ?? string.Empty;
    }

    private static void AssertRequiredFeature(IEnumerable<XElement> features, string name)
    {
        var feature = features.Single(element => string.Equals(
            AndroidAttribute(element, "name"),
            name,
            StringComparison.Ordinal));

        Assert.Equal("true", AndroidAttribute(feature, "required"));
    }

    private static void AssertContainsAll(HashSet<string> actual, string[] expected)
    {
        Assert.All(expected, value => Assert.Contains(value, actual));
    }
}
