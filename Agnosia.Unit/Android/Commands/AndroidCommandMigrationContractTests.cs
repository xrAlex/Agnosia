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
            @"internal static async Task<WorkProfileOwnerCheckResult> CheckWorkProfileOwnerAsync[\s\S]*?\n    private static async Task<WorkProfileOwnerCheckResult> TryRecoverAuthenticationAsync");

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
            @"private void HandleAction\(\)[\s\S]*?\n    private void ActionRecoverAuthentication\(\)");

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
    public void Silent_query_gateways_do_not_start_legacy_query_activities()
    {
        var profileGatewaySource = ReadAndroidSource("Gateways", "AndroidProfileCommandGateway.cs");
        var appsPagerSource = ReadAndroidSource("Gateways", "AndroidProfileAppsPager.cs");

        Assert.DoesNotContain("new Intent(AgnosiaActions.ProfilePing)", profileGatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.Query", profileGatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.Query", appsPagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_ping_activity_fallback_verifies_and_signs_results()
    {
        var activityTransportSource = ReadAndroidSource("Commands", "Transports", "ActivityCommandTransport.cs");
        var dummyRoutingSource = ReadAndroidSource("Activities", "DummyActivity.Routing.cs");

        Assert.Contains("profile_ping_unsigned", activityTransportSource, StringComparison.Ordinal);
        Assert.Contains("AuthenticationUtility.CheckIntent(result.Data)", activityTransportSource, StringComparison.Ordinal);
        Assert.Contains("if (kind == AndroidCommandKind.ProfilePing)", dummyRoutingSource, StringComparison.Ordinal);
        Assert.Contains("TrySignResult(resultIntent)", dummyRoutingSource, StringComparison.Ordinal);
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
