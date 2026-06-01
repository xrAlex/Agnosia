using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Storage;
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
        var callback = AndroidIntentExtras.ReadFileShuttleCallbackMessenger(Intent);
        if (callback is null)
        {
            Finish();
            return;
        }

        if (!IsFileShuttleActionForCurrentProfile())
        {
            SendFileShuttleConnectResult(callback, null, "File Shuttle запущен не в том профиле.");
            Finish();
            return;
        }

        if (!LocalStorageManager.Instance.GetBoolean(StorageKeys.CrossProfileFileShuttleEnabled))
        {
            SendFileShuttleConnectResult(callback, null, "File Shuttle выключен в Agnosia.");
            Finish();
            return;
        }

        if (!AndroidPermissionApi.HasAllFilesAccess(this))
        {
            SendFileShuttleConnectResult(callback, null, "Agnosia не получила доступ ко всем файлам в этом профиле.");
            Finish();
            return;
        }

        try
        {
            AgnosiaFileShuttleService.EnsureStarted(this);
            var connection = new FileShuttleServiceConnection(this, callback);
            AddFileShuttleConnection(connection);

            var intent = new Intent(this, typeof(AgnosiaFileShuttleService));
            if (BindService(intent, connection, Bind.AutoCreate)) return;

            RemoveFileShuttleConnection(connection);
            SendFileShuttleConnectResult(callback, null, "Android не смог привязаться к File Shuttle service.");
            Finish();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to start File Shuttle service: {exception}");
            SendFileShuttleConnectResult(callback, null, "Android не смог запустить File Shuttle.");
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
        Messenger? serviceMessenger,
        string? error)
    {
        try
        {
            var message = Message.Obtain(null, AgnosiaFileShuttleContract.MessageConnectResult)
                          ?? throw new InvalidOperationException(
                              "Android did not create a File Shuttle connect message.");
            var data = new Bundle();
            if (serviceMessenger is not null)
                data.PutParcelable(AgnosiaFileShuttleContract.ExtraServiceMessenger, serviceMessenger);
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
        Messenger callback) : Java.Lang.Object, IServiceConnection
    {
        public bool IsFor(DummyActivity candidate)
        {
            return ReferenceEquals(activity, candidate);
        }

        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            try
            {
                var serviceMessenger = service is null ? null : new Messenger(service);
                SendFileShuttleConnectResult(
                    callback,
                    serviceMessenger,
                    serviceMessenger is null ? "File Shuttle service не вернул Binder." : null);
            }
            finally
            {
                Disconnect();
                activity.Finish();
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
                activity.UnbindService(this);
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
