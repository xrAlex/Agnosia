using System.Collections.Concurrent;
using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Infrastructure;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Java.Lang;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using Exception = System.Exception;

namespace Agnosia.Android.Files;

internal sealed class AgnosiaFileShuttleMessengerClient
{
    private const string LogTag = "AgnosiaFileShuttleClient";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly Context _context;
    private readonly HandlerThread _handlerThread;
    private readonly Messenger _callbackMessenger;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Bundle>> _pendingRequests = [];
    private readonly Lock _connectSync = new();
    private TaskCompletionSource<Messenger>? _connectCompletion;
    private Messenger? _remoteMessenger;
    private int _nextRequestId;

    public AgnosiaFileShuttleMessengerClient(Context context)
    {
        _context = context.ApplicationContext ?? context;
        _handlerThread = new HandlerThread("AgnosiaFileShuttleClient");
        _handlerThread.Start();
        _callbackMessenger = new Messenger(new ResponseHandler(_handlerThread.Looper!, this));
    }

    public bool IsConnected => _remoteMessenger is not null;

    public void Preconnect()
    {
        try
        {
            _ = EnsureConnected();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "File Shuttle не смог подключиться к другому профилю. Откройте Files через Agnosia и проверьте доступ ко всем файлам в обоих профилях.",
                exception);
        }
    }

    public AgnosiaFileShuttleDocumentInfo? LoadFileMeta(string documentId)
    {
        var response = SendRequest(AgnosiaFileShuttleContract.MessageLoadFileMeta, data =>
            data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId));
        var json = response.GetString(AgnosiaFileShuttleContract.ExtraFileInfoJson);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize(
                json,
                AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfo);
    }

    public IReadOnlyList<AgnosiaFileShuttleDocumentInfo> LoadFiles(string parentDocumentId)
    {
        var response = SendRequest(AgnosiaFileShuttleContract.MessageLoadFiles, data =>
            data.PutString(AgnosiaFileShuttleContract.ExtraPath, parentDocumentId));
        var json = response.GetString(AgnosiaFileShuttleContract.ExtraFileListJson);
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize(
                json,
                AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfoArray) ?? [];
    }

    public ParcelFileDescriptor? OpenFile(string documentId, string mode)
    {
        var response = SendRequest(AgnosiaFileShuttleContract.MessageOpenFile, data =>
        {
            data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId);
            data.PutString(AgnosiaFileShuttleContract.ExtraMode, mode);
        });
        return ReadParcelFileDescriptor(response);
    }

    public ParcelFileDescriptor? OpenThumbnail(string documentId, Point sizeHint)
    {
        var response = SendRequest(AgnosiaFileShuttleContract.MessageOpenThumbnail, data =>
        {
            data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId);
            data.PutInt(AgnosiaFileShuttleContract.ExtraWidth, sizeHint.X);
            data.PutInt(AgnosiaFileShuttleContract.ExtraHeight, sizeHint.Y);
        });
        return ReadParcelFileDescriptor(response);
    }

    public string? CreateFile(string parentDocumentId, string mimeType, string displayName)
    {
        var response = SendRequest(AgnosiaFileShuttleContract.MessageCreateFile, data =>
        {
            data.PutString(AgnosiaFileShuttleContract.ExtraPath, parentDocumentId);
            data.PutString(AgnosiaFileShuttleContract.ExtraMimeType, mimeType);
            data.PutString(AgnosiaFileShuttleContract.ExtraDisplayName, displayName);
        });
        return response.GetString(AgnosiaFileShuttleContract.ExtraCreatedDocumentId);
    }

    public string? DeleteFile(string documentId)
    {
        var response = SendRequest(AgnosiaFileShuttleContract.MessageDeleteFile, data =>
            data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId));
        return response.GetString(AgnosiaFileShuttleContract.ExtraDeletedParentId);
    }

    public bool IsChildOf(string parentDocumentId, string documentId)
    {
        var response = SendRequest(AgnosiaFileShuttleContract.MessageIsChildOf, data =>
        {
            data.PutString(AgnosiaFileShuttleContract.ExtraParentPath, parentDocumentId);
            data.PutString(AgnosiaFileShuttleContract.ExtraChildPath, documentId);
        });
        return response.GetBoolean(AgnosiaFileShuttleContract.ExtraIsChild, false);
    }

    private Bundle SendRequest(int what, Action<Bundle> configure)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var completion = new TaskCompletionSource<Bundle>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestId] = completion;

            try
            {
                var remote = EnsureConnected();
                var data = new Bundle();
                data.PutInt(AgnosiaFileShuttleContract.ExtraRequestId, requestId);
                configure(data);

                var message = Message.Obtain(null, what)
                              ?? throw new InvalidOperationException(
                                  "Android did not create a File Shuttle request message.");
                message.ReplyTo = _callbackMessenger;
                message.Data = data;
                remote.Send(message);

                var response = WaitForResult(
                    completion.Task,
                    RequestTimeout,
                    "File Shuttle request timed out.");
                var error = response.GetString(AgnosiaFileShuttleContract.ExtraError);
                if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException(error);

                return response;
            }
            catch (RemoteException exception)
            {
                lastException = exception;
                ClearRemoteMessenger();
            }
            catch (TimeoutException exception)
            {
                lastException = exception;
                ClearRemoteMessenger();
                Log.Warn(LogTag, $"File Shuttle request timed out. what={what}, attempt={attempt + 1}.");
                throw;
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }

        throw lastException ?? new TimeoutException("File Shuttle request timed out.");
    }

    private Messenger EnsureConnected()
    {
        if (_remoteMessenger is { } remote) return remote;

        TaskCompletionSource<Messenger> completion;
        lock (_connectSync)
        {
            if (_remoteMessenger is { } lockedRemote) return lockedRemote;

            var shouldStart = _connectCompletion is null;
            completion = _connectCompletion ??= new TaskCompletionSource<Messenger>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (shouldStart) StartConnect();
        }

        try
        {
            return WaitForResult(
                completion.Task,
                ConnectTimeout,
                "File Shuttle connection timed out.");
        }
        catch (TimeoutException)
        {
            ClearRemoteMessenger();
            throw;
        }
    }

    private void StartConnect()
    {
        try
        {
            var action = AgnosiaUtilities.IsProfileOwner(_context)
                ? AgnosiaActions.StartFileShuttleWorkToParent
                : AgnosiaActions.StartFileShuttleParentToWork;
            var intent = new Intent(action);
            intent.AddFlags(ActivityFlags.NewTask);
            intent.PutExtra(AndroidCommandContract.ExtraFileShuttleCallbackMessenger, _callbackMessenger);
            AgnosiaUtilities.TransferIntentToProfile(_context, intent);

            var pendingIntent = AndroidPendingIntentApi.CreateBackgroundActivityStartPendingIntent(
                _context,
                intent,
                action);
            pendingIntent.Send(
                _context,
                Result.Ok,
                null,
                null,
                null,
                null,
                AndroidPendingIntentApi.CreateSenderBackgroundActivityStartOptions());
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to start cross-profile File Shuttle: {exception}");
            CompleteConnectWithException(exception);
        }
    }

    private void HandleMessage(Message message)
    {
        var data = message.Data ?? new Bundle();
        if (message.What == AgnosiaFileShuttleContract.MessageConnectResult)
        {
            var error = data.GetString(AgnosiaFileShuttleContract.ExtraError);
            if (!string.IsNullOrWhiteSpace(error))
            {
                CompleteConnectWithException(new InvalidOperationException(error));
                return;
            }

            if (AndroidIntentExtras.ReadMessenger(
                    data,
                    AgnosiaFileShuttleContract.ExtraServiceMessenger) is { } serviceMessenger)
            {
                _remoteMessenger = serviceMessenger;
                _connectCompletion?.TrySetResult(serviceMessenger);
                return;
            }

            CompleteConnectWithException(new InvalidOperationException("File Shuttle service did not return a messenger."));
            return;
        }

        var requestId = data.GetInt(AgnosiaFileShuttleContract.ExtraRequestId, 0);
        if (requestId > 0 && _pendingRequests.TryGetValue(requestId, out var completion))
            completion.TrySetResult(data);
    }

    private void CompleteConnectWithException(Exception exception)
    {
        lock (_connectSync)
        {
            _connectCompletion?.TrySetException(exception);
            _connectCompletion = null;
            _remoteMessenger = null;
        }
    }

    private void ClearRemoteMessenger()
    {
        lock (_connectSync)
        {
            _connectCompletion = null;
            _remoteMessenger = null;
        }
    }

    private static ParcelFileDescriptor? ReadParcelFileDescriptor(Bundle bundle)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return bundle.GetParcelable(
                AgnosiaFileShuttleContract.ExtraFileDescriptor,
                Class.FromType(typeof(ParcelFileDescriptor))) as ParcelFileDescriptor;

#pragma warning disable CA1422
        return bundle.GetParcelable(AgnosiaFileShuttleContract.ExtraFileDescriptor) as ParcelFileDescriptor;
#pragma warning restore CA1422
    }

    private static T WaitForResult<T>(Task<T> task, TimeSpan timeout, string timeoutMessage)
    {
        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
        if (!ReferenceEquals(Task.WhenAny(task, timeoutTask).GetAwaiter().GetResult(), task))
            throw new TimeoutException(timeoutMessage);

        timeoutCancellation.Cancel();
        return task.GetAwaiter().GetResult();
    }

    private sealed class ResponseHandler(Looper looper, AgnosiaFileShuttleMessengerClient client) : Handler(looper)
    {
        public override void HandleMessage(Message msg)
        {
            client.HandleMessage(msg);
        }
    }
}
