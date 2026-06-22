#if AGNOSIA_ANDROID
using System.Diagnostics;
using Agnosia.Android.Services;
using Android.Content;
using Android.OS;

namespace Agnosia.Android.Commands.Transports;

internal sealed class SilentServiceCommandTransport(Context applicationContext) : IAndroidCommandTransport
{
    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.SilentService;

    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (envelope.TargetProfile != AndroidCommandTargetProfile.Personal)
        {
            stopwatch.Stop();
            return AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                Kind,
                "Local silent service transport cannot execute commands for another profile.",
                "silent_service_wrong_profile",
                stopwatch.Elapsed,
                $"requested={envelope.TargetProfile}; supported=Personal");
        }

        var connection = new SilentCommandServiceConnection();
        var bindContext = applicationContext.ApplicationContext ?? applicationContext;
        var intent = new Intent(bindContext, typeof(SilentCommandService));

        try
        {
            if (!bindContext.BindService(intent, connection, Bind.AutoCreate))
            {
                stopwatch.Stop();
                return Unavailable(envelope, stopwatch.Elapsed, "bind=false");
            }

            try
            {
                var binder = await connection.WaitForBinderAsync(cancellationToken).ConfigureAwait(false);
                if (binder is null)
                {
                    stopwatch.Stop();
                    return Unavailable(envelope, stopwatch.Elapsed, "binder=unavailable");
                }

                var result = await binder.ExecuteAsync(envelope, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                return AsSilentServiceResult(result, stopwatch.Elapsed);
            }
            finally
            {
                Unbind(bindContext, connection);
            }
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                Kind,
                "Silent command service transport failed.",
                "silent_service_failed",
                stopwatch.Elapsed,
                exception.ToString());
        }
    }

    private static AndroidCommandResultEnvelope AsSilentServiceResult(
        AndroidCommandResultEnvelope result,
        TimeSpan elapsed)
    {
        var diagnostics = string.IsNullOrWhiteSpace(result.Diagnostics)
            ? $"serviceResultTransport={result.Transport}"
            : $"{result.Diagnostics}; serviceResultTransport={result.Transport}";

        return result with
        {
            Transport = AndroidCommandTransportKind.SilentService,
            Elapsed = elapsed,
            Diagnostics = diagnostics
        };
    }

    private static AndroidCommandResultEnvelope Unavailable(
        AndroidCommandEnvelope envelope,
        TimeSpan elapsed,
        string diagnostics)
    {
        return AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            AndroidCommandTransportKind.SilentService,
            "Silent command service is unavailable.",
            "silent_service_unavailable",
            elapsed,
            diagnostics);
    }

    private static void Unbind(Context context, SilentCommandServiceConnection connection)
    {
        try
        {
            context.UnbindService(connection);
        }
        catch (Exception)
        {
            // Best effort cleanup. Command failure should come from bind or execution.
        }
    }

    private sealed class SilentCommandServiceConnection : Java.Lang.Object, IServiceConnection
    {
        private readonly TaskCompletionSource<SilentCommandService.SilentCommandBinder?> _binderSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SilentCommandService.SilentCommandBinder?> WaitForBinderAsync(
            CancellationToken cancellationToken)
        {
            return _binderSource.Task.WaitAsync(cancellationToken);
        }

        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            _binderSource.TrySetResult(service as SilentCommandService.SilentCommandBinder);
        }

        public void OnServiceDisconnected(ComponentName? name)
        {
            _binderSource.TrySetResult(null);
        }
    }
}
#endif
