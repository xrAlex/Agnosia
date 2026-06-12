using Agnosia.Android.Api.Commands;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Commands;

public sealed class AndroidAppLaunchResultTests
{
    private const string PackageName = "org.example.app";
    private const string DisplayName = "Example";

    // Проверяет fallback display name на package name при пустом имени приложения.
    [Fact]
    public void CommandReceived_normalizes_missing_display_name_to_package_name()
    {
        var result = AndroidAppLaunchResult.CommandReceived(PackageName, " ");

        Assert.Equal(PackageName, result.PackageName);
        Assert.Equal(PackageName, result.DisplayName);
        Assert.Equal(AndroidAppLaunchStage.CommandReceived, result.Stage);
        Assert.False(result.Succeeded);
        Assert.Equal(AndroidAppLaunchIssueKind.None, result.Issue);
        Assert.Single(result.Events);
        Assert.Equal("command_received", result.Events[0].Detail);
    }

    // Проверяет placeholder для некорректной команды без package name.
    [Fact]
    public void CommandReceived_uses_unknown_placeholder_for_invalid_package()
    {
        var result = AndroidAppLaunchResult.CommandReceived(null, null);

        Assert.Equal("<unknown>", result.PackageName);
        Assert.Equal("<unknown>", result.DisplayName);
        Assert.Contains("<unknown>", result.Message, StringComparison.Ordinal);
    }

    // Проверяет, что terminal success stages переводят launch result в success.
    [Theory]
    [InlineData(AndroidAppLaunchStage.StartActivityAttempted)]
    [InlineData(AndroidAppLaunchStage.TargetBecameForeground)]
    [InlineData(AndroidAppLaunchStage.PackageRehidden)]
    public void WithStage_marks_primary_success_stages_as_successful(AndroidAppLaunchStage stage)
    {
        var result = AndroidAppLaunchResult
            .CommandReceived(PackageName, DisplayName)
            .WithStage(stage, "stage-detail");

        Assert.True(result.Succeeded);
        Assert.Equal(stage, result.Stage);
        AssertLatestEvent(result, stage, AndroidAppLaunchIssueKind.None, "stage-detail");
    }

    // Проверяет, что промежуточные и failure stages сами не помечают запуск успешным.
    [Theory]
    [InlineData(AndroidAppLaunchStage.PackageUnhidden)]
    [InlineData(AndroidAppLaunchStage.LaunchIntentResolved)]
    [InlineData(AndroidAppLaunchStage.StartActivityFailedWithException)]
    public void WithStage_keeps_intermediate_and_failure_stages_pending(AndroidAppLaunchStage stage)
    {
        var result = CreateReceivedResult().WithStage(stage);

        Assert.False(result.Succeeded);
        Assert.Equal(stage, result.Stage);
        AssertLatestEvent(result, stage);
    }

    // Проверяет, что non-fatal issue не сбрасывает уже успешный запуск.
    [Fact]
    public void WithIssue_preserves_success_for_non_fatal_issue()
    {
        var result = CreateReceivedResult()
            .WithStage(AndroidAppLaunchStage.StartActivityAttempted)
            .WithIssue(AndroidAppLaunchIssueKind.UsageAccessDenied, "usage-access");

        Assert.True(result.Succeeded);
        Assert.Equal(AndroidAppLaunchIssueKind.UsageAccessDenied, result.Issue);
        AssertLatestEvent(
            result,
            AndroidAppLaunchStage.StartActivityAttempted,
            AndroidAppLaunchIssueKind.UsageAccessDenied,
            "usage-access");
        Assert.True(result.ToOperationResult().Succeeded);
    }

    // Проверяет, что fatal issue сбрасывает success и OperationResult.
    [Fact]
    public void WithIssue_clears_success_for_fatal_issue()
    {
        var result = CreateReceivedResult()
            .WithStage(AndroidAppLaunchStage.StartActivityAttempted)
            .WithIssue(AndroidAppLaunchIssueKind.WorkProfileUnavailable, fatal: true);

        Assert.False(result.Succeeded);
        Assert.Equal(AndroidAppLaunchIssueKind.WorkProfileUnavailable, result.Issue);
        Assert.False(result.ToOperationResult().Succeeded);
    }

    // Проверяет запись failed stage, issue и message в итоговый OperationResult.
    [Fact]
    public void Fail_records_failed_stage_issue_and_operation_failure()
    {
        var result = CreateReceivedResult()
            .WithStage(AndroidAppLaunchStage.StartActivityAttempted)
            .Fail(
                AndroidAppLaunchStage.StartActivityFailedWithException,
                AndroidAppLaunchIssueKind.BackgroundActivityLaunchBlocked,
                "blocked-from-background");
        var operation = result.ToOperationResult();

        Assert.False(result.Succeeded);
        Assert.Equal(AndroidAppLaunchStage.StartActivityFailedWithException, result.Stage);
        Assert.Equal(AndroidAppLaunchIssueKind.BackgroundActivityLaunchBlocked, result.Issue);
        AssertLatestEvent(
            result,
            AndroidAppLaunchStage.StartActivityFailedWithException,
            AndroidAppLaunchIssueKind.BackgroundActivityLaunchBlocked,
            "blocked-from-background");
        Assert.False(operation.Succeeded);
        Assert.Equal(result.Message, operation.Message);
    }

    // Проверяет safe fallback при пустом или поврежденном JSON payload.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not-json")]
    public void TryReadJson_returns_unknown_command_for_missing_or_invalid_payload(string? payload)
    {
        var read = AndroidAppLaunchResult.TryReadJson(payload, out var result);

        Assert.False(read);
        AssertUnknownCommand(result);
    }

    // Проверяет классификацию Android background activity launch блокировок по тексту exception.
    [Theory]
    [InlineData("Background activity launch denied")]
    [InlineData("not allowed to start activity from background")]
    [InlineData("BAL blocked by platform")]
    public void ClassifyStartActivityException_detects_background_launch_blocks(string message)
    {
        var result = AndroidAppLaunchResult.ClassifyStartActivityException(new InvalidOperationException(message));

        Assert.Equal(AndroidAppLaunchIssueKind.BackgroundActivityLaunchBlocked, result);
    }

    // Проверяет generic fallback для неизвестных startActivity exceptions.
    [Fact]
    public void ClassifyStartActivityException_returns_generic_start_exception_without_known_signal()
    {
        var result = AndroidAppLaunchResult.ClassifyStartActivityException(new InvalidOperationException("boom"));

        Assert.Equal(AndroidAppLaunchIssueKind.StartActivityException, result);
    }

    private static AndroidAppLaunchResult CreateReceivedResult()
    {
        return AndroidAppLaunchResult.CommandReceived(PackageName, DisplayName);
    }

    private static void AssertLatestEvent(
        AndroidAppLaunchResult result,
        AndroidAppLaunchStage stage,
        AndroidAppLaunchIssueKind issue = AndroidAppLaunchIssueKind.None,
        string? detail = null)
    {
        var latestEvent = result.Events[^1];

        Assert.Equal(stage, latestEvent.Stage);
        Assert.Equal(issue, latestEvent.Issue);
        Assert.Equal(detail, latestEvent.Detail);
    }

    private static void AssertUnknownCommand(AndroidAppLaunchResult result)
    {
        Assert.Equal("<unknown>", result.PackageName);
        Assert.Equal(AndroidAppLaunchStage.CommandReceived, result.Stage);
        Assert.False(result.Succeeded);
    }
}
