using System.Collections.Concurrent;
using System.Text.Json;
using Agnosia.Android.Infrastructure;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Java.Lang;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using Exception = System.Exception;
using OperationCanceledException = System.OperationCanceledException;

namespace Agnosia.Android.Files;

internal sealed class AgnosiaFileShuttleMessengerClient
{
    private const string LogTag = "AgnosiaFileShuttleClient";
    private const int ListingMaxPages = 512;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly Context _context;
    private readonly HandlerThread _handlerThread;
    private readonly Messenger _callbackMessenger;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Bundle>> _pendingRequests = [];
    private readonly Lock _connectSync = new();
    private readonly string _clientId = Guid.NewGuid().ToString("N");
    private TaskCompletionSource<Messenger>? _connectCompletion;
    private Messenger? _remoteMessenger;
    private PendingIntent? _reconnectIntent;
    private string? _connectionId;
    private int _nextRequestId;
    private int _closed;

    public AgnosiaFileShuttleMessengerClient(Context context)
    {
        _context = context.ApplicationContext ?? context;
        _handlerThread = new HandlerThread("AgnosiaFileShuttleClient");
        _handlerThread.Start();
        _callbackMessenger = new Messenger(new ResponseHandler(_handlerThread.Looper!, this));
    }

    public bool IsConnected => _remoteMessenger is not null;
    public bool CanReconnect => Volatile.Read(ref _closed) == 0 && _reconnectIntent is not null;

    public long? LoadAvailableBytes(TimeSpan requestTimeout)
    {
        var response = SendRequest(
            AgnosiaFileShuttleContract.MessageLoadAvailableBytes,
            static _ => { },
            requestTimeout,
            requireConnected: true);
        var availableBytes = response.GetLong(AgnosiaFileShuttleContract.ExtraAvailableBytes, -1);
        return availableBytes >= 0 ? availableBytes : null;
    }

    public async Task PreconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "File Shuttle не смог подключиться к другому профилю. Откройте Files через Agnosia и проверьте доступ ко всем файлам в обоих профилях.",
                exception);
        }
    }

    public AgnosiaFileShuttleDocumentInfo? LoadFileMeta(
        string documentId,
        TimeSpan? requestTimeout = null,
        bool requireConnected = false)
    {
        var response = SendRequest(
            AgnosiaFileShuttleContract.MessageLoadFileMeta,
            data => data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId),
            requestTimeout ?? RequestTimeout,
            requireConnected);
        var json = response.GetString(AgnosiaFileShuttleContract.ExtraFileInfoJson);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize(
                json,
                AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfo);
    }

    public IReadOnlyList<AgnosiaFileShuttleDocumentInfo> LoadFiles(
        string parentDocumentId,
        TimeSpan? requestTimeout = null,
        bool requireConnected = false)
    {
        var documents = new List<AgnosiaFileShuttleDocumentInfo>();
        var offset = 0;
        string? pageToken = null;
        for (var pageIndex = 0; pageIndex < ListingMaxPages; pageIndex++)
        {
            var response = SendRequest(
                AgnosiaFileShuttleContract.MessageLoadFiles,
                data =>
                {
                    data.PutString(AgnosiaFileShuttleContract.ExtraPath, parentDocumentId);
                    data.PutInt(AgnosiaFileShuttleContract.ExtraPageOffset, offset);
                    if (pageToken is not null) data.PutString(AgnosiaFileShuttleContract.ExtraPageToken, pageToken);
                },
                requestTimeout ?? RequestTimeout,
                requireConnected);
            var json = response.GetString(AgnosiaFileShuttleContract.ExtraFileListJson);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("File Shuttle returned an incomplete directory page.");

            var page = JsonSerializer.Deserialize(
                           json,
                           AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfoArray)
                       ?? throw new InvalidOperationException("File Shuttle returned an invalid directory page.");
            documents.AddRange(page);

            if (!response.GetBoolean(AgnosiaFileShuttleContract.ExtraHasMore, false)) return documents;

            var nextOffset = response.GetInt(AgnosiaFileShuttleContract.ExtraNextPageOffset, 0);
            if (nextOffset <= offset || nextOffset != offset + page.Length)
                throw new InvalidOperationException("File Shuttle directory paging did not advance correctly.");

            offset = nextOffset;
            pageToken = response.GetString(AgnosiaFileShuttleContract.ExtraPageToken)
                ?? throw new InvalidOperationException("File Shuttle directory snapshot token is missing.");
        }

        throw new InvalidOperationException("File Shuttle directory exceeded the supported page count.");
    }

    public ParcelFileDescriptor? OpenFile(
        string documentId,
        string mode,
        TimeSpan? requestTimeout = null,
        bool requireConnected = false)
    {
        var response = SendRequest(
            AgnosiaFileShuttleContract.MessageOpenFile,
            data =>
            {
                data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId);
                data.PutString(AgnosiaFileShuttleContract.ExtraMode, mode);
            },
            requestTimeout ?? RequestTimeout,
            requireConnected);
        return ReadParcelFileDescriptor(response);
    }

    public ParcelFileDescriptor? OpenThumbnail(
        string documentId,
        Point sizeHint,
        TimeSpan? requestTimeout = null,
        bool requireConnected = false)
    {
        var response = SendRequest(
            AgnosiaFileShuttleContract.MessageOpenThumbnail,
            data =>
            {
                data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId);
                data.PutInt(AgnosiaFileShuttleContract.ExtraWidth, sizeHint.X);
                data.PutInt(AgnosiaFileShuttleContract.ExtraHeight, sizeHint.Y);
            },
            requestTimeout ?? RequestTimeout,
            requireConnected);
        return ReadParcelFileDescriptor(response);
    }

    public string? CreateFile(
        string parentDocumentId,
        string mimeType,
        string displayName,
        TimeSpan? requestTimeout = null,
        bool requireConnected = false)
    {
        var response = SendRequest(
            AgnosiaFileShuttleContract.MessageCreateFile,
            data =>
            {
                data.PutString(AgnosiaFileShuttleContract.ExtraPath, parentDocumentId);
                data.PutString(AgnosiaFileShuttleContract.ExtraMimeType, mimeType);
                data.PutString(AgnosiaFileShuttleContract.ExtraDisplayName, displayName);
            },
            requestTimeout ?? RequestTimeout,
            requireConnected);
        return response.GetString(AgnosiaFileShuttleContract.ExtraCreatedDocumentId);
    }

    public string DeleteFile(
        string documentId,
        TimeSpan? requestTimeout = null,
        bool requireConnected = false)
    {
        var response = SendRequest(
            AgnosiaFileShuttleContract.MessageDeleteFile,
            data => data.PutString(AgnosiaFileShuttleContract.ExtraPath, documentId),
            requestTimeout ?? RequestTimeout,
            requireConnected);
        return response.GetString(AgnosiaFileShuttleContract.ExtraDeletedParentId)
               ?? throw new InvalidOperationException("File Shuttle did not confirm document deletion.");
    }

    public bool IsChildOf(
        string parentDocumentId,
        string documentId,
        TimeSpan? requestTimeout = null,
        bool requireConnected = false)
    {
        var response = SendRequest(
            AgnosiaFileShuttleContract.MessageIsChildOf,
            data =>
            {
                data.PutString(AgnosiaFileShuttleContract.ExtraParentPath, parentDocumentId);
                data.PutString(AgnosiaFileShuttleContract.ExtraChildPath, documentId);
            },
            requestTimeout ?? RequestTimeout,
            requireConnected);
        return response.GetBoolean(AgnosiaFileShuttleContract.ExtraIsChild, false);
    }

    private Bundle SendRequest(int what, Action<Bundle> configure, TimeSpan requestTimeout, bool requireConnected)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var completion = new TaskCompletionSource<Bundle>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestId] = completion;
            var responseConsumed = false;
            Messenger? requestRemote = null;

            try
            {
                var remote = EnsureConnected(allowActivityStart: !requireConnected);
                requestRemote = remote;
                var data = new Bundle();
                data.PutInt(AgnosiaFileShuttleContract.ExtraRequestId, requestId);
                data.PutString(AgnosiaFileShuttleContract.ExtraConnectionId, _clientId);
                configure(data);

                var message = Message.Obtain(null, what)
                              ?? throw new InvalidOperationException(
                                  "Android did not create a File Shuttle request message.");
                message.ReplyTo = _callbackMessenger;
                message.Data = data;
                remote.Send(message);

                var response = WaitForResult(
                    completion.Task,
                    requestTimeout,
                    "File Shuttle request timed out.");
                var error = response.GetString(AgnosiaFileShuttleContract.ExtraError);
                if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException(error);
                responseConsumed = true;
                return response;
            }
            catch (RemoteException exception)
            {
                lastException = exception;
                if (requestRemote is not null) ClearRemoteMessenger(expectedRemote: requestRemote);
            }
            catch (TimeoutException)
            {
                if (requestRemote is not null) ClearRemoteMessenger(expectedRemote: requestRemote);
                Log.Warn(LogTag, $"File Shuttle request timed out. what={what}, attempt={attempt + 1}.");
                throw;
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
                if (!responseConsumed)
                    _ = completion.Task.ContinueWith(
                        static task => CloseResponseDescriptor(task.Result),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
            }
        }

        throw lastException ?? new TimeoutException("File Shuttle request timed out.");
    }

    private Messenger EnsureConnected(bool allowActivityStart)
    {
        ThrowIfClosed();
        if (_remoteMessenger is { } remote) return remote;

        TaskCompletionSource<Messenger> completion;
        lock (_connectSync)
        {
            if (_remoteMessenger is { } lockedRemote) return lockedRemote;

            var shouldStart = _connectCompletion is null;
            completion = _connectCompletion ??= new TaskCompletionSource<Messenger>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (shouldStart) StartConnect(allowActivityStart);
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
            ClearRemoteMessenger(expectedCompletion: completion);
            throw;
        }
    }

    private async Task<Messenger> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfClosed();
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
            return await completion.Task
                .WaitAsync(ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ClearRemoteMessenger(expectedCompletion: completion);
            throw;
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        lock (_connectSync)
        {
            if (_remoteMessenger is { } remote)
            {
                var message = Message.Obtain(null, AgnosiaFileShuttleContract.MessageUnregisterClient)!;
                var data = new Bundle();
                data.PutString(AgnosiaFileShuttleContract.ExtraConnectionId, _clientId);
                message.Data = data;
                try { remote.Send(message); }
                catch (RemoteException) { }
            }
            _reconnectIntent?.Cancel();
            _reconnectIntent?.Dispose();
            _reconnectIntent = null;
            _connectCompletion?.TrySetException(
                new InvalidOperationException("File Shuttle bridge was disconnected."));
            _connectCompletion = null;
            _remoteMessenger = null;
        }

        foreach (var completion in _pendingRequests.Values)
            completion.TrySetException(new InvalidOperationException("File Shuttle bridge was disconnected."));

        _handlerThread.QuitSafely();
        _callbackMessenger.Dispose();
    }

    private void StartConnect(bool allowActivityStart = true)
    {
        try
        {
            ThrowIfClosed();
            _connectionId = _clientId;
            if (_reconnectIntent is { } reconnect)
            {
                try
                {
                    reconnect.Send();
                    return;
                }
                catch (PendingIntent.CanceledException)
                {
                    _reconnectIntent = null;
                    reconnect.Dispose();
                }
            }
            if (!allowActivityStart)
                throw new InvalidOperationException("Откройте Files через Agnosia, чтобы подключить File Shuttle.");
            var action = AgnosiaUtilities.IsProfileOwner(_context)
                ? AgnosiaActions.StartFileShuttleWorkToParent
                : AgnosiaActions.StartFileShuttleParentToWork;
            var intent = new Intent(action);
            intent.AddFlags(ActivityFlags.NewTask);
            intent.PutExtra(AndroidCommandContract.ExtraFileShuttleCallbackMessenger, _callbackMessenger);
            intent.PutExtra(AgnosiaFileShuttleContract.ExtraConnectionId, _connectionId);
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
        if (Volatile.Read(ref _closed) != 0)
        {
            if (message.Data is { } discarded) CloseResponseDescriptor(discarded);
            return;
        }
        if (message.What == AgnosiaFileShuttleContract.MessageDisconnected)
        {
            var disconnected = AndroidIntentExtras.ReadMessenger(
                message.Data, AgnosiaFileShuttleContract.ExtraServiceMessenger);
            lock (_connectSync)
            {
                if (disconnected is null || _remoteMessenger?.Equals(disconnected) != true) return;
                ClearRemoteMessenger(expectedRemote: disconnected);
            }
            Log.Debug(LogTag, "File Shuttle service disconnected; reconnect capability retained.");
            return;
        }
        var data = message.Data ?? new Bundle();
        if (message.What == AgnosiaFileShuttleContract.MessageConnectResult)
        {
            lock (_connectSync)
            {
                if (_connectionId is null || !string.Equals(_connectionId,
                        data.GetString(AgnosiaFileShuttleContract.ExtraConnectionId), StringComparison.Ordinal)) return;
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
                    if (Volatile.Read(ref _closed) != 0) return;
                    var reconnect = OperatingSystem.IsAndroidVersionAtLeast(33)
                        ? data.GetParcelable(AgnosiaFileShuttleContract.ExtraReconnectIntent,
                            Class.FromType(typeof(PendingIntent))) as PendingIntent
#pragma warning disable CA1422
                        : data.GetParcelable(AgnosiaFileShuttleContract.ExtraReconnectIntent) as PendingIntent;
#pragma warning restore CA1422
                    if (reconnect is not null) _reconnectIntent = reconnect;
                    _remoteMessenger = serviceMessenger;
                    _connectionId = null;
                    _connectCompletion?.TrySetResult(serviceMessenger);
                    _connectCompletion = null;
                    return;
                }

                CompleteConnectWithException(new InvalidOperationException("File Shuttle service did not return a messenger."));
                return;
            }
        }

        var requestId = data.GetInt(AgnosiaFileShuttleContract.ExtraRequestId, 0);
        if (requestId > 0 && _pendingRequests.TryGetValue(requestId, out var completion))
            if (completion.TrySetResult(data)) return;

        CloseResponseDescriptor(data);
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref _closed) != 0)
            throw new InvalidOperationException("File Shuttle bridge was disconnected.");
    }

    private static void CloseResponseDescriptor(Bundle response)
    {
        using var descriptor = ReadParcelFileDescriptor(response);
        try { descriptor?.Close(); }
        catch (Java.IO.IOException exception)
        {
            Log.Warn(LogTag, $"Failed to close an unused File Shuttle descriptor: {exception.Message}");
        }
    }

    private void CompleteConnectWithException(Exception exception)
    {
        lock (_connectSync)
        {
            _connectionId = null;
            _connectCompletion?.TrySetException(exception);
            _connectCompletion = null;
            _remoteMessenger = null;
        }
    }

    private void ClearRemoteMessenger(
        TaskCompletionSource<Messenger>? expectedCompletion = null,
        Messenger? expectedRemote = null)
    {
        lock (_connectSync)
        {
            if (expectedCompletion is not null && !ReferenceEquals(_connectCompletion, expectedCompletion)) return;
            if (expectedRemote is not null && _remoteMessenger?.Equals(expectedRemote) != true) return;
            _connectionId = null;
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
