using Agnosia.Android.Files;
using Agnosia.Android.Infrastructure;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private static readonly Lock FileShuttleConnectionSync = new();
    private static readonly HashSet<FileShuttleServiceConnection> FileShuttleConnections = [];

    private void ActionStartFileShuttle()
    {
        FileShuttleServiceConnection? connection = null;
        var callback = AndroidIntentExtras.ReadFileShuttleCallbackMessenger(Intent);
        var connectionId = Intent?.GetStringExtra(AgnosiaFileShuttleContract.ExtraConnectionId);
        if (callback is null)
        {
            Finish();
            return;
        }

        if (!IsFileShuttleActionForCurrentProfile())
        {
            SendFileShuttleConnectResult(callback, connectionId, null, "File Shuttle запущен не в том профиле.");
            Finish();
            return;
        }

        if (!ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.CrossProfileFileShuttleEnabled))
        {
            SendFileShuttleConnectResult(callback, connectionId, null, "File Shuttle выключен в Agnosia.");
            Finish();
            return;
        }

        if (!AndroidPermissionApi.HasAllFilesAccess(this))
        {
            SendFileShuttleConnectResult(callback, connectionId, null, "Agnosia не получила доступ ко всем файлам в этом профиле.");
            Finish();
            return;
        }

        try
        {
            AgnosiaFileShuttleService.EnsureStarted(this);
            connection = new FileShuttleServiceConnection(this, ApplicationContext ?? this, callback, connectionId);
            AddFileShuttleConnection(connection);

            var intent = new Intent(this, typeof(AgnosiaFileShuttleService));
            intent.PutExtra(AndroidCommandContract.ExtraFileShuttleCallbackMessenger, callback);
            intent.PutExtra(AgnosiaFileShuttleContract.ExtraConnectionId, connectionId);
            if ((ApplicationContext ?? this).BindService(intent, connection, Bind.AutoCreate)) return;

            RemoveFileShuttleConnection(connection);
            SendFileShuttleConnectResult(callback, connectionId, null, "Android не смог привязаться к File Shuttle service.");
            Finish();
        }
        catch (Exception exception)
        {
            if (connection is not null) RemoveFileShuttleConnection(connection);
            Log.Warn(LogTag, $"Failed to start File Shuttle service: {exception}");
            SendFileShuttleConnectResult(callback, connectionId, null, "Android не смог запустить File Shuttle.");
            Finish();
        }
    }

    private bool IsFileShuttleActionForCurrentProfile()
    {
        return Intent?.Action switch
        {
            AgnosiaActions.StartFileShuttleParentToWork => _isProfileOwner,
            AgnosiaActions.StartFileShuttleWorkToParent => !_isProfileOwner,
            _ => false
        };
    }

    private static void AddFileShuttleConnection(FileShuttleServiceConnection connection)
    {
        lock (FileShuttleConnectionSync)
        {
            FileShuttleConnections.Add(connection);
        }
    }

    private static void RemoveFileShuttleConnection(FileShuttleServiceConnection connection)
    {
        lock (FileShuttleConnectionSync)
        {
            FileShuttleConnections.Remove(connection);
        }
    }

    private void CloseFileShuttleConnections()
    {
        FileShuttleServiceConnection[] connections;
        lock (FileShuttleConnectionSync)
        {
            connections = FileShuttleConnections
                .Where(connection => connection.IsFor(this))
                .ToArray();
        }

        foreach (var connection in connections) connection.Disconnect();
    }

    private static void SendFileShuttleConnectResult(
        Messenger callback,
        string? connectionId,
        Messenger? serviceMessenger,
        string? error,
        PendingIntent? reconnectIntent = null)
    {
        try
        {
            var message = Message.Obtain(null, AgnosiaFileShuttleContract.MessageConnectResult)
                          ?? throw new InvalidOperationException(
                              "Android did not create a File Shuttle connect message.");
            var data = new Bundle();
            data.PutString(AgnosiaFileShuttleContract.ExtraConnectionId, connectionId);
            if (serviceMessenger is not null)
                data.PutParcelable(AgnosiaFileShuttleContract.ExtraServiceMessenger, serviceMessenger);
            if (reconnectIntent is not null)
                data.PutParcelable(AgnosiaFileShuttleContract.ExtraReconnectIntent, reconnectIntent);
            if (!string.IsNullOrWhiteSpace(error))
                data.PutString(AgnosiaFileShuttleContract.ExtraError, error);
            message.Data = data;
            callback.Send(message);
        }
        catch (RemoteException exception)
        {
            Log.Warn(LogTag, $"Failed to send File Shuttle connect result: {exception.Message}");
        }
    }

    private sealed class FileShuttleServiceConnection(
        DummyActivity activity,
        Context bindingContext,
        Messenger callback,
        string? connectionId) : Java.Lang.Object, IServiceConnection
    {
        private DummyActivity? _activity = activity;

        public bool IsFor(DummyActivity candidate)
        {
            return ReferenceEquals(_activity, candidate);
        }

        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            var activityToFinish = _activity;
            try
            {
                var serviceMessenger = service is null ? null : new Messenger(service);
                SendFileShuttleConnectResult(
                    callback,
                    connectionId,
                    serviceMessenger,
                    serviceMessenger is null ? "File Shuttle service не вернул Binder." : null,
                    serviceMessenger is null ? null : AgnosiaFileShuttleService.CreateReconnectIntent(
                        bindingContext, callback, connectionId));
                _activity = null;
                Disconnect();
            }
            finally
            {
                activityToFinish?.Finish();
            }
        }

        public void OnServiceDisconnected(ComponentName? name)
        {
            Disconnect();
        }

        public void Disconnect()
        {
            try
            {
                bindingContext.UnbindService(this);
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, $"Failed to unbind File Shuttle service: {exception.Message}");
            }
            finally
            {
                RemoveFileShuttleConnection(this);
            }
        }
    }
}
