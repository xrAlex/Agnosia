using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Agnosia.Android.Api.Commands;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Android;

public sealed class AndroidManifestContractTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    // Проверяет, что action-группы синхронизированы с IntentFilter Android-активностей.
    [Fact]
    public void Target_profile_activity_actions_are_declared_by_android_intent_filters()
    {
        var actionNamesByValue = NamesByValueOf(typeof(AgnosiaActions));
        var expectedActionNames = AgnosiaActions.TargetProfileActivityActions
            .Select(action => actionNamesByValue[action])
            .Order(StringComparer.Ordinal)
            .ToArray();
        var intentFilterActionNames = ReadIntentFilterActionNames(
            "Activities\\DummyActivity.cs",
            "Activities\\ProxyActivity.cs");

        Assert.Empty(expectedActionNames.Except(intentFilterActionNames, StringComparer.Ordinal));
        Assert.Empty(intentFilterActionNames.Except(expectedActionNames, StringComparer.Ordinal));
    }

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
                "android.permission.FOREGROUND_SERVICE_DATA_SYNC",
                "android.permission.FOREGROUND_SERVICE_SPECIAL_USE",
                "android.permission.FOREGROUND_SERVICE_SYSTEM_EXEMPTED",
                "android.permission.PACKAGE_USAGE_STATS",
                "android.permission.RECEIVE_BOOT_COMPLETED",
                "android.permission.REQUEST_INSTALL_PACKAGES",
                "android.permission.REQUEST_DELETE_PACKAGES",
                "android.permission.MANAGE_EXTERNAL_STORAGE",
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

    // Проверяет source-contract для DocumentsProvider, который попадает в итоговый manifest через C# attributes.
    [Fact]
    public void File_shuttle_documents_provider_contract_is_declared_in_source()
    {
        var source = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Files", "AgnosiaCrossProfileDocumentsProvider.cs"));

        Assert.Contains("[ContentProvider(", source, StringComparison.Ordinal);
        Assert.Contains("Name = AgnosiaFileShuttleContract.ProviderComponentName", source, StringComparison.Ordinal);
        Assert.Contains("Exported = true", source, StringComparison.Ordinal);
        Assert.Contains("Enabled = false", source, StringComparison.Ordinal);
        Assert.Contains("GrantUriPermissions = true", source, StringComparison.Ordinal);
        Assert.Contains("Permission = Manifest.Permission.ManageDocuments", source, StringComparison.Ordinal);
        Assert.Contains("android.content.action.DOCUMENTS_PROVIDER", source, StringComparison.Ordinal);
    }

    // Проверяет source-contract для локального bound service File Shuttle.
    [Fact]
    public void File_shuttle_service_contract_is_declared_in_source()
    {
        var source = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Files", "AgnosiaFileShuttleService.cs"));

        Assert.Contains("[Service(", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"com.agnosia.app.AgnosiaFileShuttleService\"", source, StringComparison.Ordinal);
        Assert.Contains("Exported = false", source, StringComparison.Ordinal);
        Assert.Contains("ForegroundServiceType = ForegroundService.TypeDataSync", source, StringComparison.Ordinal);
        Assert.Contains("context.StartService(intent)", source, StringComparison.Ordinal);
        Assert.Contains("StartForeground(NotificationId, notification, ForegroundService.TypeDataSync)", source,
            StringComparison.Ordinal);
        Assert.Contains("public override IBinder? OnBind", source, StringComparison.Ordinal);
        Assert.Contains("return _messenger?.Binder", source, StringComparison.Ordinal);
    }

    // Проверяет, что cross-profile File Shuttle стартует через PendingIntent с BAL opt-in.
    [Fact]
    public void File_shuttle_cross_profile_start_uses_pending_intent_bal_opt_in()
    {
        var clientSource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Files", "AgnosiaFileShuttleMessengerClient.cs"));
        var brokerSource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Files", "AgnosiaFileShuttleClientBroker.cs"));
        var providerSource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Files", "AgnosiaCrossProfileDocumentsProvider.cs"));
        var settingsSource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Platform", "AndroidSettingsCoordinator.cs"));
        var pendingIntentSource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android.Api", "Packages", "AndroidPendingIntentApi.cs"));
        var preconnectIndex = settingsSource.IndexOf(
            "AgnosiaFileShuttleClientBroker.Preconnect(activity)",
            StringComparison.Ordinal);
        var startDocumentsIndex = settingsSource.IndexOf("AndroidIntentApi.TryStartActivity", StringComparison.Ordinal);

        Assert.Contains("CreateBackgroundActivityStartPendingIntent", clientSource, StringComparison.Ordinal);
        Assert.Contains("CreateSenderBackgroundActivityStartOptions", clientSource, StringComparison.Ordinal);
        Assert.Contains("public void Preconnect()", clientSource, StringComparison.Ordinal);
        Assert.Contains("GetClient(Context context)", brokerSource, StringComparison.Ordinal);
        Assert.Contains("_client ??= new AgnosiaFileShuttleMessengerClient(context)", brokerSource, StringComparison.Ordinal);
        Assert.Contains("AgnosiaFileShuttleClientBroker.GetClient(Context)", providerSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestTimeout", providerSource, StringComparison.Ordinal);
        Assert.Contains("if (!client.IsConnected)", providerSource, StringComparison.Ordinal);
        Assert.Contains("requireConnected: true", providerSource, StringComparison.Ordinal);
        Assert.Contains("manual Files launches are best-effort", providerSource, StringComparison.Ordinal);
        Assert.InRange(preconnectIndex, 0, startDocumentsIndex - 1);
        Assert.DoesNotContain("_context.StartActivity(intent)", clientSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Preconnect(", providerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StartConnect(", providerSource, StringComparison.Ordinal);
        Assert.Contains("PendingIntent.GetActivity", pendingIntentSource, StringComparison.Ordinal);
        Assert.Contains("SetPendingIntentBackgroundActivityStartMode", pendingIntentSource, StringComparison.Ordinal);
        Assert.Contains(
            "SetPendingIntentCreatorBackgroundActivityStartMode",
            pendingIntentSource,
            StringComparison.Ordinal);
    }

    // Проверяет, что VpnService component живет в Android-проекте и сохраняет Android VPN contract.
    [Fact]
    public void Transient_vpn_disconnect_service_contract_is_declared_in_android_project()
    {
        var source = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Vpn", "TransientVpnDisconnectService.cs"));

        Assert.Contains("[Service(", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"com.agnosia.app.TransientVpnDisconnectService\"", source,
            StringComparison.Ordinal);
        Assert.Contains("Permission = \"android.permission.BIND_VPN_SERVICE\"", source, StringComparison.Ordinal);
        Assert.Contains("ForegroundServiceType = ForegroundService.TypeSystemExempted", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[IntentFilter([\"android.net.VpnService\"])]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("android.net.VpnService.SUPPORTS_ALWAYS_ON", source,
            StringComparison.Ordinal);
        Assert.Contains("ActionVpnService = \"android.net.VpnService\"", source, StringComparison.Ordinal);
        Assert.Contains("base.OnBind(intent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public override IBinder? OnBind(Intent? intent)\n    {\n        return null;", source,
            StringComparison.Ordinal);
        Assert.False(File.Exists(
            RepositoryPaths.Get("Agnosia.Android.Api", "Vpn", "TransientVpnDisconnectService.cs")));
    }

    [Fact]
    public void Lockdown_vpn_service_contract_is_declared_in_android_project()
    {
        var source = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Vpn", "LockdownVpnService.cs"));
        var startupSource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Infrastructure", "AndroidStartup.cs"));

        Assert.Contains("[Service(", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"com.agnosia.app.LockdownVpnService\"", source,
            StringComparison.Ordinal);
        Assert.Contains("Permission = \"android.permission.BIND_VPN_SERVICE\"", source, StringComparison.Ordinal);
        Assert.Contains("ForegroundServiceType = ForegroundService.TypeSystemExempted", source,
            StringComparison.Ordinal);
        Assert.Contains("ActionVpnService = \"android.net.VpnService\"", source, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([ActionVpnService])]", source, StringComparison.Ordinal);
        Assert.Contains("ActionVpnManagerEvent = \"android.net.action.VPN_MANAGER_EVENT\"", source,
            StringComparison.Ordinal);
        Assert.Contains("CategoryEventAlwaysOnStateChanged = \"android.net.category.EVENT_ALWAYS_ON_STATE_CHANGED\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([ActionVpnManagerEvent], Categories = [CategoryEventAlwaysOnStateChanged])]",
            source,
            StringComparison.Ordinal);
        Assert.Contains("[MetaData(\"android.net.VpnService.SUPPORTS_ALWAYS_ON\", Value = \"true\")]", source,
            StringComparison.Ordinal);
        Assert.Contains("base.OnBind(intent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public override IBinder? OnBind(Intent? intent)\n    {\n        return null;", source,
            StringComparison.Ordinal);
        Assert.Contains("builder.AddDisallowedApplication(packageName)", source, StringComparison.Ordinal);
        Assert.Contains("LockdownVpnController.EnsureEnabledPolicy(context", startupSource,
            StringComparison.Ordinal);
        Assert.False(File.Exists(
            RepositoryPaths.Get("Agnosia.Android.Api", "Vpn", "LockdownVpnService.cs")));
    }

    [Fact]
    public void Vpn_permission_snapshot_does_not_call_prepare()
    {
        var source = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android", "Permissions", "AndroidPermissionCoordinator.cs"));
        var loadPermissions = Regex.Match(
            source,
            @"public async Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync[\s\S]*?\n    public async Task<OperationResult> RequestPermissionAsync",
            RegexOptions.Singleline).Value;
        var requestVpnControl = Regex.Match(
            source,
            @"private async Task<OperationResult> RequestVpnControlAsync[\s\S]*?\n    private Task<OperationResult> RequestPackageInstallAccessAsync",
            RegexOptions.Singleline).Value;

        Assert.Contains("StorageKeys.VpnControlPrepared", loadPermissions, StringComparison.Ordinal);
        Assert.DoesNotContain("VpnService.Prepare", loadPermissions, StringComparison.Ordinal);
        Assert.DoesNotContain("IsVpnPrepared", loadPermissions, StringComparison.Ordinal);
        Assert.Contains("VpnService.Prepare(activity)", requestVpnControl, StringComparison.Ordinal);
        Assert.Contains("StorageKeys.VpnControlPrepared", requestVpnControl, StringComparison.Ordinal);
    }

    private static XDocument ReadManifest()
    {
        return XDocument.Load(RepositoryPaths.Get("Agnosia.Android", "Properties", "AndroidManifest.xml"));
    }

    private static string ReadMainActivitySource()
    {
        return File.ReadAllText(RepositoryPaths.Get("Agnosia.Android", "MainActivity.cs"));
    }

    private static string ReadAndroidSource(string relativeSourcePath)
    {
        return File.ReadAllText(RepositoryPaths.Get("Agnosia.Android", relativeSourcePath));
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

    private static Dictionary<string, string> NamesByValueOf(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => new
            {
                Name = field.Name,
                Value = (string?)field.GetRawConstantValue() ?? string.Empty
            })
            .ToDictionary(pair => pair.Value, pair => pair.Name, StringComparer.Ordinal);
    }

    private static string[] ReadIntentFilterActionNames(params string[] relativeSourcePaths)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relativeSourcePath in relativeSourcePaths)
        {
            var source = ReadAndroidSource(relativeSourcePath);
            var filters = Regex.Matches(
                source,
                @"\[IntentFilter\((?<body>.*?)\)\]",
                RegexOptions.Singleline);

            foreach (Match filter in filters)
            foreach (Match action in Regex.Matches(
                         filter.Groups["body"].Value,
                         @"AgnosiaActions\.(?<name>[A-Za-z0-9_]+)"))
                names.Add(action.Groups["name"].Value);
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }
}
