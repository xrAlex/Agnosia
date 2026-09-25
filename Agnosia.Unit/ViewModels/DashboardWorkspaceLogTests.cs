using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceLogTests
{
    [Fact]
    public async Task Closing_logs_during_read_does_not_reopen_the_window()
    {
        var read = new TaskCompletionSource<IReadOnlyList<AppLogEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            LoadLogsHandler = () => { started.SetResult(); return read.Task; }
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        viewModel.LoggingEnabled = true;

        var opening = viewModel.OpenLogsCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var wasOpenWhileLoading = viewModel.IsLogWindowOpen;
        viewModel.CloseLogsCommand.Execute(null);
        read.SetResult([OldEntry()]);
        await opening;

        Assert.True(wasOpenWhileLoading);
        Assert.False(viewModel.IsLogWindowOpen);
    }

    [Fact]
    public async Task Failed_log_read_preserves_entries_and_releases_dashboard_refresh()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(settings: AppSettingsSnapshot.Default with { LoggingEnabled = true }),
            RecentLogs = [OldEntry()]
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        await viewModel.OpenLogsCommand.ExecuteAsync(null);
        services.LoadLogsHandler = () => throw new InvalidOperationException("unavailable archive");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDashboardRefreshing);
        Assert.False(viewModel.IsOperationActive);
        Assert.False(viewModel.StatusIsError);
        Assert.True(viewModel.HasLogLoadError);
        Assert.Contains("old-event", viewModel.LogOutput);

        services.LoadLogsHandler = null;
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsDashboardRefreshing);
        Assert.False(viewModel.StatusIsError);
        Assert.False(viewModel.HasLogLoadError);
    }

    [Fact]
    public async Task A_log_read_failure_does_not_prevent_refresh_on_resume()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(settings: AppSettingsSnapshot.Default with { LoggingEnabled = true }),
            LoadLogsHandler = () => throw new InvalidOperationException("temporarily unavailable")
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        await viewModel.OpenLogsCommand.ExecuteAsync(null);
        var previousLoads = services.DashboardProfileLoadCount;
        services.LoadLogsHandler = null;

        viewModel.HandlePrimaryActivityResumed();

        await AsyncAssert.EventuallyAsync(() => services.DashboardProfileLoadCount > previousLoads,
            "A transient log failure must not disable dashboard refresh on resume.");
    }

    [Fact]
    public async Task Clearing_logs_removes_archives_before_reopening()
    {
        var services = new TestPlatformServices { RecentLogs = [OldEntry()] };
        var viewModel = TestWorkspaceFactory.Create(services);
        viewModel.LoggingEnabled = true;
        await viewModel.OpenLogsCommand.ExecuteAsync(null);
        Assert.Contains("old-event", viewModel.LogOutput);

        await ClearAsync(viewModel);
        await viewModel.OpenLogsCommand.ExecuteAsync(null);

        Assert.Empty(services.RecentLogs);
        Assert.DoesNotContain("old-event", viewModel.LogOutput);
    }

    [Fact]
    public async Task Read_started_before_clear_cannot_restore_deleted_entries()
    {
        var read = new TaskCompletionSource<IReadOnlyList<AppLogEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            LoadLogsHandler = () => { started.SetResult(); return read.Task; }
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        viewModel.LoggingEnabled = true;
        var opening = viewModel.OpenLogsCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await ClearAsync(viewModel);
        read.SetResult([OldEntry()]);
        await opening;

        Assert.DoesNotContain("old-event", viewModel.LogOutput);
    }

    [Fact]
    public async Task Failed_archive_clear_is_reported()
    {
        var services = new TestPlatformServices
        {
            ClearLogsHandler = () => Task.FromResult(OperationResult.Failure("work archive unavailable"))
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await ClearAsync(viewModel);
        Assert.Contains("work archive unavailable", viewModel.StatusMessage);
    }

    private static AppLogEntry OldEntry() => new("old", DateTimeOffset.Now,
        ProfileKind.Work, AppLogLevel.Information, "test", "old-event");

    private static Task ClearAsync(DashboardWorkspaceViewModel viewModel)
    {
        if (viewModel.ClearLogsCommand is IAsyncRelayCommand asyncCommand)
            return asyncCommand.ExecuteAsync(null);
        viewModel.ClearLogsCommand.Execute(null);
        return Task.CompletedTask;
    }
}
