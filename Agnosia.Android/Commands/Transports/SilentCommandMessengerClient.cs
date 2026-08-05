#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using Agnosia.Android.Services;
using Android.Content;
using Android.OS;

namespace Agnosia.Android.Commands.Transports;

internal static class SilentCommandMessengerClient
{
    public const int MessageExecuteCommand = 1;
    public const int MessageCommandResult = 2;
    public const string CommandJsonKey = "agnosia.command.json";
    public const string ResultJsonKey = "agnosia.command.result_json";

    public static async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandTransportKind transport,
        Func<IServiceConnection, bool> bindService,
        Action<IServiceConnection> unbindService,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var connection = new SilentCommandServiceConnection();
        var bindAttempted = false;

        try
        {
            bindAttempted = true;
            var bound = bindService(connection);
            if (!bound)
                return Unavailable(envelope, transport, stopwatch.Elapsed, "bind=false");

            var serviceMessenger = await connection.WaitForMessengerAsync(cancellationToken).ConfigureAwait(false);
            if (serviceMessenger is null)
                return Unavailable(envelope, transport, stopwatch.Elapsed, "binder=unavailable");

            using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            replyTimeout.CancelAfter(GetReplyTimeout(envelope));

            AndroidCommandResultEnvelope result;
            try
            {
                result = await SendAsync(serviceMessenger, envelope, replyTimeout.Token).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                           && replyTimeout.IsCancellationRequested)
            {
                stopwatch.Stop();
                return AndroidCommandResultEnvelope.Failure(
                    envelope.CorrelationId,
                    envelope.Kind,
                    transport,
                    "Silent command service did not reply before fallback timeout.",
                    "silent_service_reply_timeout",
                    stopwatch.Elapsed,
                    $"replyTimeoutMs={GetReplyTimeout(envelope).TotalMilliseconds:0}");
            }

            var identity = CommandResultEnvelopeIdentity.Validate(envelope, result);
            if (!identity.Succeeded)
            {
                stopwatch.Stop();
                return AndroidCommandResultEnvelope.Failure(
                    envelope.CorrelationId,
                    envelope.Kind,
                    transport,
                    "Silent command service returned a result for a different command.",
                    identity.ErrorCode!,
                    stopwatch.Elapsed,
                    $"actualCorrelationId={result.CorrelationId}; actualKind={result.Kind}");
            }

            stopwatch.Stop();
            return AsTransportResult(result, transport, stopwatch.Elapsed);
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
                transport,
                "Silent command service transport failed.",
                "silent_service_failed",
                stopwatch.Elapsed,
                exception.ToString());
        }
        finally
        {
            if (bindAttempted) Unbind(unbindService, connection);
        }
    }

    public static async Task<AndroidCommandResultEnvelope> ExecuteCommandJsonAsync(
        string? commandJson,
        AndroidCommandTransportKind transport,
        Func<AndroidCommandEnvelope, CancellationToken, Task<AndroidCommandResultEnvelope>> executeAsync,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commandJson))
            return AndroidCommandResultEnvelope.Failure(
                Guid.Empty,
                AndroidCommandKind.ProfilePing,
                transport,
                "Silent command request was empty.",
                "command_request_missing",
                TimeSpan.Zero,
                string.Empty);

        try
        {
            var envelope = JsonSerializer.Deserialize<AndroidCommandEnvelope>(commandJson);
            if (envelope is null)
                return AndroidCommandResultEnvelope.Failure(
                    Guid.Empty,
                    AndroidCommandKind.ProfilePing,
                    transport,
                    "Silent command request could not be decoded.",
                    "command_request_invalid",
                    TimeSpan.Zero,
                    string.Empty);

            try
            {
                return await executeAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return AndroidCommandResultEnvelope.Failure(
                    envelope.CorrelationId,
                    envelope.Kind,
                    transport,
                    "Silent command handler failed.",
                    "command_handler_exception",
                    TimeSpan.Zero,
                    exception.ToString());
            }
        }
        catch (JsonException exception)
        {
            return AndroidCommandResultEnvelope.Failure(
                Guid.Empty,
                AndroidCommandKind.ProfilePing,
                transport,
                "Silent command request was not valid JSON.",
                "command_request_invalid",
                TimeSpan.Zero,
                exception.Message);
        }
    }

    private static TimeSpan GetReplyTimeout(AndroidCommandEnvelope envelope)
    {
        return AndroidCommandReplyTimeoutPolicy.GetReplyTimeout(envelope);
    }

    public static Message CreateResultMessage(AndroidCommandResultEnvelope result)
    {
        var data = new Bundle();
        data.PutString(ResultJsonKey, JsonSerializer.Serialize(result));

        var message = Message.Obtain(null, MessageCommandResult)!;
        message.Data = data;
        return message;
    }

    private static async Task<AndroidCommandResultEnvelope> SendAsync(
        Messenger serviceMessenger,
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var handlerThread = new HandlerThread("AgnosiaSilentCommandReply");
        handlerThread.Start();

        try
        {
            var replyHandler = new SilentCommandReplyHandler(handlerThread.Looper!);
            using var registration = cancellationToken.Register(
                static state => ((SilentCommandReplyHandler)state!).Cancel(),
                replyHandler);
            var replyMessenger = new Messenger(replyHandler);
            var requestData = new Bundle();
            requestData.PutString(CommandJsonKey, JsonSerializer.Serialize(envelope));

            var request = Message.Obtain(null, MessageExecuteCommand)!;
            request.ReplyTo = replyMessenger;
            request.Data = requestData;
            serviceMessenger.Send(request);

            return await replyHandler.WaitForResultAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            handlerThread.QuitSafely();
        }
    }

    private static AndroidCommandResultEnvelope AsTransportResult(
        AndroidCommandResultEnvelope result,
        AndroidCommandTransportKind transport,
        TimeSpan elapsed)
    {
        var diagnostics = string.IsNullOrWhiteSpace(result.Diagnostics)
            ? $"serviceResultTransport={result.Transport}"
            : $"{result.Diagnostics}; serviceResultTransport={result.Transport}";

        return result with
        {
            Transport = transport,
            Elapsed = elapsed,
            Diagnostics = diagnostics
        };
    }

    private static AndroidCommandResultEnvelope Unavailable(
        AndroidCommandEnvelope envelope,
        AndroidCommandTransportKind transport,
        TimeSpan elapsed,
        string diagnostics)
    {
        return AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            transport,
            "Silent command service is unavailable.",
            "silent_service_unavailable",
            elapsed,
            diagnostics);
    }

    private static void Unbind(Action<IServiceConnection> unbindService, IServiceConnection connection)
    {
        try
        {
            unbindService(connection);
        }
        catch (Exception)
        {
            // Best effort cleanup. Command failure should come from bind or execution.
        }
    }

    private sealed class SilentCommandServiceConnection : Java.Lang.Object, IServiceConnection
    {
        private readonly TaskCompletionSource<Messenger?> _messengerSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Messenger?> WaitForMessengerAsync(CancellationToken cancellationToken)
        {
            return _messengerSource.Task.WaitAsync(cancellationToken);
        }

        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            _messengerSource.TrySetResult(service is null ? null : new Messenger(service));
        }

        public void OnServiceDisconnected(ComponentName? name)
        {
            _messengerSource.TrySetResult(null);
        }
    }

    private sealed class SilentCommandReplyHandler(Looper looper) : Handler(looper)
    {
        private readonly TaskCompletionSource<AndroidCommandResultEnvelope> _resultSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void HandleMessage(Message msg)
        {
            if (msg.What != MessageCommandResult)
            {
                base.HandleMessage(msg);
                return;
            }

            try
            {
                var json = msg.Data?.GetString(ResultJsonKey);
                var result = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<AndroidCommandResultEnvelope>(json);
                if (result is null)
                {
                    _resultSource.TrySetException(new InvalidOperationException("Silent command result was empty."));
                    return;
                }

                _resultSource.TrySetResult(result);
            }
            catch (Exception exception)
            {
                _resultSource.TrySetException(exception);
            }
        }

        public Task<AndroidCommandResultEnvelope> WaitForResultAsync(CancellationToken cancellationToken)
        {
            return _resultSource.Task.WaitAsync(cancellationToken);
        }

        public void Cancel()
        {
            _resultSource.TrySetCanceled();
        }
    }
}
#endif
