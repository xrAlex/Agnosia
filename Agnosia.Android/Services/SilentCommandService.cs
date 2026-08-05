#if AGNOSIA_ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Agnosia.Android.Commands.Transports;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

[Service(
    Name = SilentCommandService.ServiceName,
    Exported = true,
    Permission = "com.agnosia.app.permission.CROSS_PROFILE_COMMAND")]
public sealed class SilentCommandService : Service
{
    private const string LogTag = "AgnosiaSilentCommandService";
    public const string ServiceName = "com.agnosia.app.SilentCommandService";

    private Messenger? _messenger;

    public override IBinder OnBind(Intent? intent)
    {
        AgnosiaRuntime.Initialize(this);
        _messenger ??= new Messenger(new SilentCommandHandler(this));
        return _messenger.Binder!;
    }

    private sealed class SilentCommandHandler(SilentCommandService service) : Handler(Looper.MainLooper!)
    {
        public override void HandleMessage(Message msg)
        {
            if (msg.What != SilentCommandMessengerClient.MessageExecuteCommand)
            {
                base.HandleMessage(msg);
                return;
            }

            var commandJson = msg.Data?.GetString(SilentCommandMessengerClient.CommandJsonKey);
            var replyTo = msg.ReplyTo;
            _ = HandleCommandAsync(service, commandJson, replyTo);
        }
    }

    private static async Task HandleCommandAsync(
        SilentCommandService service,
        string? commandJson,
        Messenger? replyTo)
    {
        AndroidCommandResultEnvelope result;
        try
        {
            result = await SilentCommandMessengerClient.ExecuteCommandJsonAsync(
                    commandJson,
                    AndroidCommandTransportKind.SilentService,
                    (envelope, cancellationToken) =>
                    {
                        var contextFactory = ServiceRegistry.GetRequiredService<AndroidCommandExecutionContextFactory>();
                        var executor = ServiceRegistry.GetRequiredService<AndroidCommandHandlerExecutor>();
                        var context = contextFactory.Create(
                            service,
                            null,
                            envelope,
                            AndroidCommandTransportKind.SilentService,
                            "service");

                        return executor.ExecuteAsync(envelope, context, cancellationToken);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warn(
                LogTag,
                $"Silent command execution failed before reply. error={exception.GetType().Name}: {exception.Message}");
            result = AndroidCommandResultEnvelope.Failure(
                Guid.Empty,
                AndroidCommandKind.ProfilePing,
                AndroidCommandTransportKind.SilentService,
                "Silent command service failed before handler execution.",
                "command_service_exception",
                TimeSpan.Zero,
                exception.ToString());
        }

        TrySendReply(replyTo, result);
    }

    private static void TrySendReply(Messenger? replyTo, AndroidCommandResultEnvelope result)
    {
        try
        {
            replyTo?.Send(SilentCommandMessengerClient.CreateResultMessage(result));
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to send silent command reply: {exception.Message}");
        }
    }
}
#endif
