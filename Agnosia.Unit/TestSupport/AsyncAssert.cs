using System.Diagnostics;
using Avalonia.Threading;
using Xunit;

namespace Agnosia.Unit.TestSupport;

internal static class AsyncAssert
{
    public static async Task EventuallyAsync(
        Func<bool> condition,
        string because,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(2);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            if (Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.RunJobs();
            if (condition()) return;

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        if (Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), because);
    }
}
