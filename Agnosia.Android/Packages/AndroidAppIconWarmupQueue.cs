using System.Threading.Channels;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Packages;

public static class AndroidAppIconWarmupQueue
{
    private const string LogTag = "AgnosiaIconWarmup";
    private static readonly TimeSpan HungIconLogDelay = TimeSpan.FromSeconds(2);
    private static readonly Lock Sync = new();
    private static readonly Channel<WarmupRequest> Pending = Channel.CreateUnbounded<WarmupRequest>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private static readonly HashSet<string> InFlight = new(StringComparer.Ordinal);
    private static bool _processorRunning;

    public static byte[]? TryLoadCachedOrQueue(
        Context context,
        PackageManager packageManager,
        string packageName)
    {
        var cachedIcon = AndroidAppInventoryApi.TryLoadCachedAppIconPng(context, packageManager, packageName);
        if (cachedIcon is { Length: > 0 }) return cachedIcon;

        QueueWarmup(context, packageManager, packageName);
        return null;
    }

    private static void QueueWarmup(
        Context context,
        PackageManager packageManager,
        string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return;

        var appContext = context.ApplicationContext ?? context;
        lock (Sync)
        {
            if (!InFlight.Add(packageName)) return;

            Pending.Writer.TryWrite(new WarmupRequest(appContext, packageManager, packageName));
            if (_processorRunning) return;

            _processorRunning = true;
        }

        _ = ProcessQueueAsync();
    }

    private static async Task ProcessQueueAsync()
    {
        while (TryDequeue(out var request))
            await WarmupAsync(request).ConfigureAwait(false);
    }

    private static bool TryDequeue(out WarmupRequest request)
    {
        lock (Sync)
        {
            if (Pending.Reader.TryRead(out var pendingRequest))
            {
                request = pendingRequest;
                return true;
            }

            _processorRunning = false;
            request = null!;
            return false;
        }
    }

    private static async Task WarmupAsync(WarmupRequest request)
    {
        try
        {
            var loadTask = Task.Run(() => AndroidAppInventoryApi.LoadAppIconPng(
                request.Context,
                request.PackageManager,
                request.PackageName,
                CancellationToken.None));
            using var delayCancellation = new CancellationTokenSource();
            var delayTask = Task.Delay(HungIconLogDelay, delayCancellation.Token);
            if (await Task.WhenAny(loadTask, delayTask).ConfigureAwait(false) == delayTask)
                Log.Debug(
                    LogTag,
                    $"Icon load is still running after {HungIconLogDelay.TotalMilliseconds:0}ms. package={request.PackageName}.");
            else
                await delayCancellation.CancelAsync();

            await loadTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Debug(LogTag, $"Icon warm-up failed. package={request.PackageName}, error={exception.GetType().Name}.");
        }
        finally
        {
            lock (Sync)
            {
                InFlight.Remove(request.PackageName);
            }
        }
    }

    private sealed record WarmupRequest(Context Context, PackageManager PackageManager, string PackageName);
}
