using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceIconLoadingTests
{
    // Проверяет объединение нескольких запросов иконки одного package в один batch load.
    [Fact]
    public async Task LoadAppIconPngAsync_batches_multiple_requests_for_same_package()
    {
        var delays = new ManualDelayScheduler();
        var icon = new byte[] { 1, 2, 3 };
        var services = new TestPlatformServices
        {
            LoadAppIconsHandler = (apps, _) =>
            {
                IReadOnlyDictionary<AppItemKey, byte[]?> result = apps.ToDictionary(
                    AppItemKey.FromSnapshot, byte[]? (_) => icon,
                    EqualityComparer<AppItemKey>.Default);

                return Task.FromResult(result);
            }
        };
        var viewModel = TestWorkspaceFactory.Create(services, delays.DelayAsync);
        var first = TestSnapshots.App(ProfileKind.Personal, "com.example.same", "Same");
        var second = TestSnapshots.App(ProfileKind.Personal, "com.example.same", "Same Again");

        var firstTask = viewModel.LoadAppIconPngAsync(first, CancellationToken.None);
        var secondTask = viewModel.LoadAppIconPngAsync(second, CancellationToken.None);
        delays.CompleteNext();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(icon, results[0]);
        Assert.Same(icon, results[1]);
        var request = Assert.Single(services.AppIconLoadRequests);
        var app = Assert.Single(request);
        Assert.Equal("com.example.same", app.PackageName);
    }

    // Проверяет отмену pending-запроса иконки до выполнения batch load.
    [Fact]
    public async Task LoadAppIconPngAsync_cancels_pending_request_before_batch_load()
    {
        var delays = new ManualDelayScheduler();
        var services = new TestPlatformServices();
        var viewModel = TestWorkspaceFactory.Create(services, delays.DelayAsync);
        var app = TestSnapshots.App(ProfileKind.Personal, "com.example.cancel", "Cancel");
        using var cancellation = new CancellationTokenSource();

        var task = viewModel.LoadAppIconPngAsync(app, cancellation.Token);
        await cancellation.CancelAsync();
        delays.CompleteNext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);

        Assert.Empty(services.AppIconLoadRequests);
    }

    // Проверяет проброс ошибки batch load в ожидающий task загрузки иконки.
    [Fact]
    public async Task LoadAppIconPngAsync_propagates_batch_load_failure_to_waiter()
    {
        var delays = new ManualDelayScheduler();
        var expected = new InvalidOperationException("icon bridge failed");
        var services = new TestPlatformServices
        {
            LoadAppIconsHandler = (_, _) =>
                Task.FromException<IReadOnlyDictionary<AppItemKey, byte[]?>>(expected)
        };
        var viewModel = TestWorkspaceFactory.Create(services, delays.DelayAsync);
        var app = TestSnapshots.App(ProfileKind.Personal, "com.example.error", "Error");

        var iconTask = viewModel.LoadAppIconPngAsync(app, CancellationToken.None);
        delays.CompleteNext();
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await iconTask);

        Assert.Same(expected, actual);
    }

    // Проверяет, что batch-key учитывает профиль и не смешивает personal/work package twins.
    [Fact]
    public async Task LoadAppIconPngAsync_keeps_personal_and_work_icons_separate_for_same_package()
    {
        var delays = new ManualDelayScheduler();
        var personalIcon = new byte[] { 1 };
        var workIcon = new byte[] { 2 };
        var services = new TestPlatformServices
        {
            LoadAppIconsHandler = (_, _) => Task.FromResult<IReadOnlyDictionary<AppItemKey, byte[]?>>(
                new Dictionary<AppItemKey, byte[]?>
                {
                    [new AppItemKey(ProfileKind.Personal, "com.example.same")] = personalIcon,
                    [new AppItemKey(ProfileKind.Work, "com.example.same")] = workIcon
                })
        };
        var viewModel = TestWorkspaceFactory.Create(services, delays.DelayAsync);
        var personal = TestSnapshots.App(ProfileKind.Personal, "com.example.same", "Same");
        var work = TestSnapshots.App(ProfileKind.Work, "com.example.same", "Same Work");

        var personalTask = viewModel.LoadAppIconPngAsync(personal, CancellationToken.None);
        var workTask = viewModel.LoadAppIconPngAsync(work, CancellationToken.None);
        delays.CompleteNext();

        Assert.Same(personalIcon, await personalTask);
        Assert.Same(workIcon, await workTask);

        var request = Assert.Single(services.AppIconLoadRequests);
        Assert.Equal(2, request.Count);
        Assert.Contains(request, app => app.Profile == ProfileKind.Personal && app.PackageName == "com.example.same");
        Assert.Contains(request, app => app.Profile == ProfileKind.Work && app.PackageName == "com.example.same");
    }
}
