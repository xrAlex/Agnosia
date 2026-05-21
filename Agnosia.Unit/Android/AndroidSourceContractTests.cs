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
