using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceLogTests
{
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
