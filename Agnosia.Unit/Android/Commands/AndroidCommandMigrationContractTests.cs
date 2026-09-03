using System.Text.RegularExpressions;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandMigrationContractTests
{
    [Fact]
    public void Work_profile_ping_is_executed_through_command_center()
    {
        var source = ReadAndroidSource("Gateways", "AndroidProfileCommandGateway.cs");
        var method = MatchRequired(
            source,
            @"internal static async Task<WorkProfileOwnerCheckResult> CheckWorkProfileOwnerAsync[\s\S]*?\n    internal static async Task<ProfileAppsQueryResult\?> QueryAppsAsync");

        Assert.Contains("AndroidCommandKind.ProfilePing", method, StringComparison.Ordinal);
        Assert.Contains("ServiceRegistry.GetRequiredService<AndroidCommandCenter>()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.ProfilePing)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StartActivityForResultAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DummyActivity_routes_migrated_ping_and_icon_queries_through_command_handlers()
    {
        var source = ReadAndroidSource("Activities", "DummyActivity.Routing.cs");
        var handleAction = MatchRequired(
            source,
            @"private void HandleAction\(\)[\s\S]*?\n    private static void TrySignResult\(Intent result\)");

        AssertRoutesCommand(handleAction, "ProfilePing", "ProfilePing");
        AssertRoutesCommand(handleAction, "QueryApps", "QueryApps");
        AssertRoutesCommand(handleAction, "QueryAppIcon", "QueryAppIcon");
        AssertRoutesCommand(handleAction, "QueryAppIcons", "QueryAppIcons");
        AssertRoutesCommand(handleAction, "QueryLogs", "QueryLogs");
        AssertRoutesCommand(handleAction, "QueryCrossProfilePackages", "QueryCrossProfilePackages");
        AssertRoutesCommand(handleAction, "QueryPermissions", "QueryPermissions");
        AssertRoutesCommand(handleAction, "QueryUsageStatsAccess", "QueryPermissions");
        AssertRoutesCommand(handleAction, "QueryPackageInstallAccess", "QueryPermissions");
        AssertRoutesCommand(handleAction, "QueryAllFilesAccess", "QueryPermissions");
        Assert.DoesNotContain("FinishWithProfileOwnerCheck();", handleAction, StringComparison.Ordinal);
        Assert.DoesNotContain("RunAction(ActionQueryAppIconAsync", handleAction, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionQueryUsageStatsAccess();", handleAction, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionQueryPackageInstallAccess();", handleAction, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionQueryAllFilesAccess();", handleAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_work_icon_query_uses_command_center_not_activity_transport()
    {
        var source = ReadAndroidSource("Gateways", "AndroidProfileCommandGateway.cs");
        var method = MatchRequired(
            source,
            @"internal static async Task<byte\[\]\?> LoadAppIconAsync[\s\S]*?\n    internal static async Task<IReadOnlyDictionary<AppItemKey, byte\[\]\?>> LoadAppIconsAsync");

        Assert.Contains("AndroidCommandKind.QueryAppIcon", method, StringComparison.Ordinal);
        Assert.Contains("ServiceRegistry.GetRequiredService<AndroidCommandCenter>()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.QueryAppIcon)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AndroidProfileCommandTransport.StartForDataAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_query_gateways_use_command_center_instead_of_legacy_intents()
    {
        var profileGatewaySource = ReadAndroidSource("Gateways", "AndroidProfileCommandGateway.cs");
        var appsPagerSource = ReadAndroidSource("Gateways", "AndroidProfileAppsPager.cs");

        Assert.DoesNotContain("new Intent(AgnosiaActions.ProfilePing)", profileGatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.Query", profileGatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.Query", appsPagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_activity_commands_use_authenticated_dpm_forwarder()
    {
        var gatewaySource = ReadAndroidSource("Gateways", "AndroidActivityCommandGateway.cs");
        var hostSource = ReadAndroidSource("Gateways", "IAndroidActivityHost.cs");
        var startMethod = MatchRequired(
            gatewaySource,
            @"public async Task<AndroidActivityResult> StartActivityForResultAsync[\s\S]*?\n    private static AndroidActivityResult CreateWorkProfileTimeoutResult");

        Assert.DoesNotContain("AndroidSystemApi.GetCrossProfileApps", startMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CanInteractAcrossProfiles", startMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("StartCrossProfileForResultAsync", startMethod, StringComparison.Ordinal);
        Assert.Contains("AgnosiaUtilities.TransferIntentToProfile(activity, intent)", startMethod,
            StringComparison.Ordinal);
        Assert.Contains("RunForwardedWorkProfileActivityCommandAsync", startMethod, StringComparison.Ordinal);
        Assert.InRange(
            startMethod.IndexOf("PrepareAuthenticatedCommand", StringComparison.Ordinal),
            0,
            startMethod.IndexOf("AgnosiaUtilities.TransferIntentToProfile", StringComparison.Ordinal) - 1);
        Assert.DoesNotContain("StartCrossProfileForResultAsync", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_command_gateway_authenticates_every_remote_result()
    {
        var gatewaySource = ReadAndroidSource("Gateways", "AndroidActivityCommandGateway.cs");
        var activityTransportSource = ReadAndroidSource("Commands", "Transports", "ActivityCommandTransport.cs");
        var dummyResultsSource = ReadAndroidSource("Activities", "DummyActivity.Results.cs");

        Assert.Contains("AuthenticationUtility.CheckIntent(data)", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("ActivityCommandResultIdentity.Validate", gatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("envelope.Kind == AndroidCommandKind.ProfilePing", activityTransportSource,
            StringComparison.Ordinal);
        Assert.Contains("AgnosiaActions.CommandResult", dummyResultsSource, StringComparison.Ordinal);
        Assert.Contains("AndroidCommandContract.ExtraCommandCorrelationId", dummyResultsSource,
            StringComparison.Ordinal);
        Assert.Contains("AndroidCommandContract.ExtraCommandKind", dummyResultsSource, StringComparison.Ordinal);
        Assert.Contains("AndroidCommandContract.ResultCommandResultCode", dummyResultsSource,
            StringComparison.Ordinal);
        Assert.Contains("TrySignResult(data)", dummyResultsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_command_preserves_handler_kind_when_result_identity_uses_action_kind()
    {
        var source = ReadAndroidSource("Activities", "DummyActivity.Routing.cs");
        var method = MatchRequired(
            source,
            @"private void RunCommandCenterAction[\s\S]*?\n    }");

        Assert.Matches(@"_commandCorrelationId,\s+kind,", method);
        Assert.DoesNotMatch(@"_commandCorrelationId,\s+_commandKind,", method);
    }

    [Fact]
    public void QueryAppIcon_handler_is_registered()
    {
        var source = ReadAndroidSource("Infrastructure", "AndroidServiceCollectionExtensions.cs");

        Assert.Contains("QueryAppIconCommandHandler", source, StringComparison.Ordinal);
        Assert.Contains("AndroidCommandKind.QueryAppIcon", ReadAndroidSource("Commands", "Handlers", "QueryAppIconCommandHandler.cs"), StringComparison.Ordinal);
    }

    private static string ReadAndroidSource(params string[] relativePath)
    {
        return File.ReadAllText(RepositoryPaths.Get(["Agnosia.Android", ..relativePath]));
    }

    private static void AssertRoutesCommand(
        string handleAction,
        string actionName,
        string commandName)
    {
        var caseBody = MatchRequired(
            handleAction,
            @$"case AgnosiaActions\.{actionName}:[\s\S]*?break;");
        Assert.Contains(
            $"RunCommandCenterAction(AndroidCommandKind.{commandName}",
            caseBody,
            StringComparison.Ordinal);
    }

    private static string MatchRequired(string source, string pattern)
    {
        var match = Regex.Match(source, pattern, RegexOptions.Singleline);
        return match.Success
            ? match.Value
            : throw new InvalidOperationException($"Pattern not found: {pattern}");
    }
}
