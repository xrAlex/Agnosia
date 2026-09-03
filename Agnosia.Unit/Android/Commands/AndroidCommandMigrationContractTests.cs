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
    public void Authentication_recovery_uses_only_silent_command_center_transport()
    {
        var source = ReadAndroidSource("Gateways", "AndroidProfileCommandGateway.cs");
        var method = MatchRequired(
            source,
            @"private static async Task<WorkProfileOwnerCheckResult> TryRecoverAuthenticationAsync[\s\S]*?\n    internal static async Task<ProfileAppsQueryResult\?> QueryAppsAsync");

        Assert.Contains("AndroidCommandKind.RecoverAuthentication", method, StringComparison.Ordinal);
        Assert.Contains("AndroidCommandInteractivity.Silent", method, StringComparison.Ordinal);
        Assert.Contains("AuthenticationKeyMaterial.Create()", method, StringComparison.Ordinal);
        Assert.Contains("ServiceRegistry.GetRequiredService<AndroidCommandCenter>()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StartUnsignedWorkProfileActivityForResultAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAndStoreKey", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Authentication_recovery_has_no_unsigned_activity_entry_point()
    {
        var activityGateway = ReadAndroidSource("Gateways", "AndroidActivityCommandGateway.cs");
        var utilities = ReadAndroidSource("Platform", "AgnosiaUtilities.cs");
        var dummyActivity = ReadAndroidSource("Activities", "DummyActivity.cs");
        var dummyRouting = ReadAndroidSource("Activities", "DummyActivity.Routing.cs");

        Assert.DoesNotContain("StartUnsignedWorkProfileActivityForResultAsync", activityGateway,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TransferIntentToProfileWithoutAuthentication", utilities, StringComparison.Ordinal);
        Assert.DoesNotContain("AgnosiaActions.RecoverAuthentication", dummyActivity, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionRecoverAuthentication", dummyRouting, StringComparison.Ordinal);
    }

    [Fact]
    public void Authentication_recovery_restarts_work_profile_policy_safety_nets_after_key_rotation()
    {
        var handler = ReadAndroidSource("Commands", "Handlers", "RecoverAuthenticationCommandHandler.cs");

        Assert.Contains("AuthenticationUtility.TryStoreProvisioningKey", handler, StringComparison.Ordinal);
        Assert.Contains("AndroidStartup.EnsureWorkProfilePoliciesAndStartLockFreezeMonitor", handler,
            StringComparison.Ordinal);
        Assert.InRange(
            handler.IndexOf("AuthenticationUtility.TryStoreProvisioningKey", StringComparison.Ordinal),
            0,
            handler.IndexOf("AndroidStartup.EnsureWorkProfilePoliciesAndStartLockFreezeMonitor",
                StringComparison.Ordinal) - 1);
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
    public void Silent_query_gateways_do_not_start_legacy_query_activities()
    {
        var profileGatewaySource = ReadAndroidSource("Gateways", "AndroidProfileCommandGateway.cs");
        var appsPagerSource = ReadAndroidSource("Gateways", "AndroidProfileAppsPager.cs");

        Assert.DoesNotContain("new Intent(AgnosiaActions.ProfilePing)", profileGatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.Query", profileGatewaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Intent(AgnosiaActions.Query", appsPagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_activity_commands_prefer_explicit_cross_profile_apps_and_fallback_to_authenticated_dpm_forwarder()
    {
        var gatewaySource = ReadAndroidSource("Gateways", "AndroidActivityCommandGateway.cs");
        var hostSource = ReadAndroidSource("Gateways", "IAndroidActivityHost.cs");
        var startMethod = MatchRequired(
            gatewaySource,
            @"public async Task<AndroidActivityResult> StartActivityForResultAsync[\s\S]*?\n    private static AndroidActivityResult CreateWorkProfileTimeoutResult");

        Assert.Contains("AndroidSystemApi.GetCrossProfileApps", startMethod, StringComparison.Ordinal);
        Assert.Contains("CanInteractAcrossProfiles", startMethod, StringComparison.Ordinal);
        Assert.Contains("intent.SetComponent", startMethod, StringComparison.Ordinal);
        Assert.Contains("StartCrossProfileForResultAsync", startMethod, StringComparison.Ordinal);
        Assert.Contains("AgnosiaUtilities.TransferIntentToProfile(activity, intent)", startMethod,
            StringComparison.Ordinal);
        Assert.Contains("RunForwardedWorkProfileActivityCommandAsync", startMethod, StringComparison.Ordinal);
        Assert.InRange(
            startMethod.IndexOf("PrepareAuthenticatedCommand", StringComparison.Ordinal),
            0,
            startMethod.IndexOf("AgnosiaUtilities.TransferIntentToProfile", StringComparison.Ordinal) - 1);
        Assert.Contains("StartCrossProfileForResultAsync", hostSource, StringComparison.Ordinal);
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
