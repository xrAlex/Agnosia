using System.Text.RegularExpressions;
using System.Xml.Linq;
using Agnosia.Android.Api.Commands;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Android;

public sealed class AndroidSourceContractTests
{
    // Проверяет, что action-группы синхронизированы с IntentFilter Android-активностей.
    [Fact]
    public void Target_profile_activity_actions_are_declared_by_android_intent_filters()
    {
        var actionNamesByValue = StringConstantContract.NamesByValueOf(typeof(AgnosiaActions));
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

    // Проверяет связку DeviceAdminReceiver с permission и XML policy resource.
    [Fact]
    public void Device_admin_receiver_is_bound_to_device_admin_permission_and_policy_resource()
    {
        var receiverSource = ReadAndroidSource("Receivers\\AgnosiaDeviceAdminReceiver.cs");
        var policyDocument = XDocument.Load(
            RepositoryPaths.Get("Agnosia.Android", "Resources", "xml", "device_admin.xml"));
        var policyNames = policyDocument
            .Descendants("uses-policies")
            .Elements()
            .Select(element => element.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Permission = Manifest.Permission.BindDeviceAdmin", receiverSource, StringComparison.Ordinal);
        Assert.Contains(
            "[MetaData(\"android.app.device_admin\", Resource = \"@xml/device_admin\")]",
            receiverSource,
            StringComparison.Ordinal);
        Assert.Contains("force-lock", policyNames);
        Assert.Contains("wipe-data", policyNames);
        Assert.Contains("disable-camera", policyNames);
    }

    // Проверяет, что boot/profile unlock не зависит только от background StartService.
    [Fact]
    public void Lock_freeze_startup_receiver_schedules_cleanup_job()
    {
        var receiverSource = ReadAndroidSource("Receivers\\LockFreezeStartupReceiver.cs");

        Assert.Contains("LockFreezeCleanupJobService.Schedule(context, action)", receiverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkProfileLockFreezeService.EnsureRunning(context)", receiverSource, StringComparison.Ordinal);
    }

    // Проверяет контракт fallback job для persisted-session cleanup после boot/profile unlock.
    [Fact]
    public void Lock_freeze_cleanup_job_runs_persisted_session_cleanup()
    {
        var jobSource = ReadAndroidSource("Services\\LockFreezeCleanupJobService.cs");
        var startupSource = ReadAndroidSource("Infrastructure\\AndroidStartup.cs");

        Assert.Contains(": JobService", jobSource, StringComparison.Ordinal);
        Assert.Contains("Permission = \"android.permission.BIND_JOB_SERVICE\"", jobSource, StringComparison.Ordinal);
        Assert.Contains("SetOverrideDeadline(0)", jobSource, StringComparison.Ordinal);
        Assert.Contains("CompletePersistedSessionForScreenLock(context)", jobSource, StringComparison.Ordinal);
        Assert.Contains("skipped_no_session", jobSource, StringComparison.Ordinal);
        Assert.Contains("LockFreezeCleanupJobService.RunStartupSafetyNet(context)", startupSource, StringComparison.Ordinal);
    }

    // Проверяет, что batch-загрузка иконок рабочего профиля идет через cross-profile command.
    [Fact]
    public void Work_profile_batch_icon_loading_uses_query_app_icons_command()
    {
        var gatewaySource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android.Api", "Gateways", "AndroidProfileCommandGateway.cs"));

        Assert.Contains("if (app.IsSystem) return null;", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("app is { Profile: ProfileKind.Work, IsSystem: false }", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("new Intent(AgnosiaActions.QueryAppIcons)", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("AndroidCommandContract.ExtraPackages", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("AndroidCommandContract.ResultIconsBundle", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("new AppItemKey(ProfileKind.Work, packageName)", gatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("icons[app.PackageName] = app.IconPng", gatewaySource, StringComparison.Ordinal);
    }

    // Проверяет, что системные иконки не грузятся ни для одного профиля.
    [Fact]
    public void System_app_icons_are_disabled_globally()
    {
        var inventorySource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android.Api", "Packages", "AndroidAppInventoryApi.cs"));
        var gatewaySource = File.ReadAllText(
            RepositoryPaths.Get("Agnosia.Android.Api", "Gateways", "AndroidProfileCommandGateway.cs"));

        Assert.Contains("AndroidWorkProfilePackageClassifier.IsSystemApp(app)) return null;", inventorySource, StringComparison.Ordinal);
        Assert.Contains("loadIcon: false", inventorySource, StringComparison.Ordinal);
        Assert.Contains("app is { Profile: ProfileKind.Personal, IsSystem: false }", gatewaySource, StringComparison.Ordinal);
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

    private static string ReadAndroidSource(string relativeSourcePath)
    {
        return File.ReadAllText(RepositoryPaths.Get("Agnosia.Android", relativeSourcePath));
    }
}
