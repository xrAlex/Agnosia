using System.Text.Json;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using Environment = Android.OS.Environment;
using File = Java.IO.File;

namespace Agnosia.Android.Files;

[Service(
    Name = "com.agnosia.app.AgnosiaFileShuttleService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class AgnosiaFileShuttleService : Service
{
    private const string LogTag = "AgnosiaFileShuttle";
    private const int NotificationId = 4037;
    private const string NotificationChannelId = "agnosia_file_shuttle";
    private const string NotificationChannelName = "File Shuttle";
    private const string NotificationChannelDescription = "Передача файлов между личным и рабочим профилями.";
    private static readonly TimeSpan IdleStopDelay = TimeSpan.FromSeconds(30);

    private HandlerThread? _handlerThread;
    private Handler? _handler;
    private Messenger? _messenger;
    private int _idleStopGeneration;

    public override void OnCreate()
    {
        base.OnCreate();

        _handlerThread = new HandlerThread("AgnosiaFileShuttle");
        _handlerThread.Start();
        _handler = new FileShuttleHandler(_handlerThread.Looper!, this);
        _messenger = new Messenger(_handler);
        StartForegroundServiceNotification();
        ResetIdleStop();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        ResetIdleStop();
        return StartCommandResult.NotSticky;
    }

    public override IBinder? OnBind(Intent? intent)
    {
        ResetIdleStop();
        return _messenger?.Binder;
    }

    public override void OnDestroy()
    {
        _handler?.RemoveCallbacksAndMessages(null);
        _handlerThread?.QuitSafely();
        _messenger?.Dispose();
        base.OnDestroy();
    }

    public static void EnsureStarted(Context context)
    {
        var intent = new Intent(context, typeof(AgnosiaFileShuttleService));
        context.StartService(intent);
    }

    private void StartForegroundServiceNotification()
    {
        var smallIcon = ApplicationInfo?.Icon ?? ResourceConstant.Mipmap.ic_launcher;
        var notification = AndroidNotificationApi.BuildNotification(
            this,
            NotificationChannelId,
            NotificationChannelName,
            NotificationChannelDescription,
            "Agnosia File Shuttle",
            "DocumentsUI разрешен доступ к передаче файлов между профилями.",
            smallIcon,
            minimized: true);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
            return;
        }

        StartForeground(NotificationId, notification);
    }

    private void ResetIdleStop()
    {
        var generation = Interlocked.Increment(ref _idleStopGeneration);
        _handler?.PostDelayed(
            () =>
            {
                if (generation == Volatile.Read(ref _idleStopGeneration)) StopSelf();
            },
            (long)IdleStopDelay.TotalMilliseconds);
    }

    private void HandleRequest(Message message)
    {
        ResetIdleStop();

        var response = Message.Obtain(null, message.What)
                       ?? throw new InvalidOperationException("Android did not create a File Shuttle response message.");
        var responseData = new Bundle();
        var requestId = message.Data?.GetInt(AgnosiaFileShuttleContract.ExtraRequestId, 0) ?? 0;
        responseData.PutInt(AgnosiaFileShuttleContract.ExtraRequestId, requestId);

        ParcelFileDescriptor? responseDescriptor = null;
        try
        {
            responseDescriptor = WriteResponse(message.What, message.Data ?? new Bundle(), responseData);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"File shuttle request failed. what={message.What}, requestId={requestId}, error={exception}");
            responseData.PutString(AgnosiaFileShuttleContract.ExtraError, "Android не смог обработать запрос File Shuttle.");
        }

        response.Data = responseData;

        try
        {
            message.ReplyTo?.Send(response);
        }
        catch (RemoteException exception)
        {
            Log.Warn(LogTag, $"Failed to send File Shuttle response: {exception.Message}");
        }
        finally
        {
            responseDescriptor?.Dispose();
        }
    }

    private ParcelFileDescriptor? WriteResponse(int what, Bundle request, Bundle response)
    {
        switch (what)
        {
            case AgnosiaFileShuttleContract.MessageLoadFileMeta:
                response.PutString(
                    AgnosiaFileShuttleContract.ExtraFileInfoJson,
                    JsonSerializer.Serialize(
                        LoadFileMeta(request.GetString(AgnosiaFileShuttleContract.ExtraPath)),
                        AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfo));
                return null;

            case AgnosiaFileShuttleContract.MessageLoadFiles:
                response.PutString(
                    AgnosiaFileShuttleContract.ExtraFileListJson,
                        JsonSerializer.Serialize(
                            LoadFiles(request.GetString(AgnosiaFileShuttleContract.ExtraPath)),
                            AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfoArray));
                return null;

            case AgnosiaFileShuttleContract.MessageOpenFile:
            {
                var descriptor = OpenFile(
                    request.GetString(AgnosiaFileShuttleContract.ExtraPath),
                    request.GetString(AgnosiaFileShuttleContract.ExtraMode) ?? "r");
                response.PutParcelable(
                    AgnosiaFileShuttleContract.ExtraFileDescriptor,
                    descriptor);
                return descriptor;
            }

            case AgnosiaFileShuttleContract.MessageOpenThumbnail:
            {
                var descriptor = OpenThumbnail(request.GetString(AgnosiaFileShuttleContract.ExtraPath));
                response.PutParcelable(
                    AgnosiaFileShuttleContract.ExtraFileDescriptor,
                    descriptor);
                return descriptor;
            }

            case AgnosiaFileShuttleContract.MessageCreateFile:
                response.PutString(
                    AgnosiaFileShuttleContract.ExtraCreatedDocumentId,
                    CreateFile(
                        request.GetString(AgnosiaFileShuttleContract.ExtraPath),
                        request.GetString(AgnosiaFileShuttleContract.ExtraMimeType),
                        request.GetString(AgnosiaFileShuttleContract.ExtraDisplayName)));
                return null;

            case AgnosiaFileShuttleContract.MessageDeleteFile:
                response.PutString(
                    AgnosiaFileShuttleContract.ExtraDeletedParentId,
                    DeleteDocumentFile(request.GetString(AgnosiaFileShuttleContract.ExtraPath)));
                return null;

            case AgnosiaFileShuttleContract.MessageIsChildOf:
                response.PutBoolean(
                    AgnosiaFileShuttleContract.ExtraIsChild,
                    IsChildOf(
                        request.GetString(AgnosiaFileShuttleContract.ExtraParentPath),
                        request.GetString(AgnosiaFileShuttleContract.ExtraChildPath)));
                return null;

            default:
                response.PutString(AgnosiaFileShuttleContract.ExtraError, "Неизвестный запрос File Shuttle.");
                return null;
        }
    }

    private IReadOnlyList<AgnosiaFileShuttleDocumentInfo> LoadFiles(string? path)
    {
        var directory = ResolveFile(path, requireExists: true);
        if (directory is null || !directory.IsDirectory) return [];

        var children = directory.ListFiles();
        if (children is null) return [];

        var result = new List<AgnosiaFileShuttleDocumentInfo>(children.Length);
        foreach (var child in children)
            if (IsWithinExternalStorage(child))
                result.Add(CreateDocumentInfo(child));

        return result
            .OrderBy(static info => info.MimeType == DocumentsContract.Document.MimeTypeDir ? 0 : 1)
            .ThenBy(static info => info.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private AgnosiaFileShuttleDocumentInfo LoadFileMeta(string? path)
    {
        var file = ResolveFile(path, requireExists: true)
                   ?? throw new System.IO.FileNotFoundException("File Shuttle path is outside shared storage.");
        return CreateDocumentInfo(file, IsDummyRoot(path));
    }

    private ParcelFileDescriptor? OpenFile(string? path, string mode)
    {
        var file = ResolveFile(path, requireExists: true);
        if (file is null || file.IsDirectory) return null;

        return ParcelFileDescriptor.Open(file, ParcelFileDescriptor.ParseMode(mode));
    }

    private ParcelFileDescriptor? OpenThumbnail(string? path)
    {
        var file = ResolveFile(path, requireExists: true);
        if (file is null || file.IsDirectory) return null;

        var mimeType = GetMimeType(file);
        return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly)
            : null;
    }

    private string? CreateFile(string? parentPath, string? mimeType, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        var parent = ResolveFile(parentPath, requireExists: true);
        if (parent is null || !parent.IsDirectory) return null;

        var isDirectory = string.Equals(mimeType, DocumentsContract.Document.MimeTypeDir, StringComparison.Ordinal);
        var target = CreateChildTarget(parent, displayName, mimeType, isDirectory);
        if (target is null) return null;

        var created = isDirectory ? target.Mkdir() : target.CreateNewFile();
        return created ? target.CanonicalPath : null;
    }

    private string? DeleteDocumentFile(string? path)
    {
        var file = ResolveFile(path, requireExists: true);
        if (file is null || IsExternalStorageRoot(file)) return null;

        var parent = file.ParentFile?.CanonicalPath;
        return DeleteRecursively(file) ? parent : null;
    }

    private bool IsChildOf(string? parentPath, string? childPath)
    {
        var parent = ResolveFile(parentPath, requireExists: true);
        var child = ResolveFile(childPath, requireExists: true);
        if (parent is null || child is null || !parent.IsDirectory) return false;

        var parentCanonical = parent.CanonicalPath.TrimEnd('/') + "/";
        return string.Equals(parent.CanonicalPath, child.CanonicalPath, StringComparison.Ordinal)
               || child.CanonicalPath.StartsWith(parentCanonical, StringComparison.Ordinal);
    }

    private AgnosiaFileShuttleDocumentInfo CreateDocumentInfo(File file, bool forceDummyRootId = false)
    {
        var isDirectory = file.IsDirectory;
        var mimeType = isDirectory ? DocumentsContract.Document.MimeTypeDir : GetMimeType(file);
        var isRoot = forceDummyRootId || IsExternalStorageRoot(file);
        var flags = isDirectory
            ? DocumentContractFlags.DirSupportsCreate
            : DocumentContractFlags.SupportsWrite;

        if (!isRoot) flags |= DocumentContractFlags.SupportsDelete;

        if (!isDirectory && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            flags |= DocumentContractFlags.SupportsThumbnail;

        return new AgnosiaFileShuttleDocumentInfo(
            forceDummyRootId ? AgnosiaFileShuttleContract.DummyRoot : file.CanonicalPath,
            IsExternalStorageRoot(file) ? "Agnosia" : file.Name,
            mimeType,
            isDirectory ? 0 : file.Length(),
            file.LastModified(),
            (int)flags);
    }

    private File? ResolveFile(string? path, bool requireExists = false)
    {
        var root = Environment.ExternalStorageDirectory;
        if (root is null) return null;

        var rootPath = root.CanonicalPath;
        var candidatePath = MapDocumentIdToPath(path, rootPath);

        if (string.IsNullOrWhiteSpace(candidatePath)) return null;

        var candidate = new File(candidatePath);
        if (!IsWithinExternalStorage(candidate, rootPath)) return null;
        if (requireExists && !candidate.Exists()) return null;

        return candidate;
    }

    private static string? MapDocumentIdToPath(string? path, string rootPath)
    {
        if (!IsDummyRoot(path)) return path;

        var suffix = path!.Length > AgnosiaFileShuttleContract.DummyRoot.Length
            ? path[AgnosiaFileShuttleContract.DummyRoot.Length..].TrimStart('/')
            : string.Empty;
        return string.IsNullOrEmpty(suffix)
            ? rootPath
            : rootPath.TrimEnd('/') + "/" + suffix;
    }

    private static bool IsDummyRoot(string? path)
    {
        return string.Equals(path, AgnosiaFileShuttleContract.DummyRoot, StringComparison.Ordinal)
               || path?.StartsWith(AgnosiaFileShuttleContract.DummyRoot, StringComparison.Ordinal) == true;
    }

    private static File? CreateChildTarget(File parent, string displayName, string? mimeType, bool isDirectory)
    {
        var candidateName = AppendExtensionIfNeeded(displayName, mimeType, isDirectory);
        if (!IsSafeDisplayName(candidateName)) return null;

        var target = EnsureUniqueTarget(new File(parent, candidateName), isDirectory);
        return IsDirectChild(parent, target) && IsWithinExternalStorage(target) ? target : null;
    }

    private static bool IsSafeDisplayName(string displayName)
    {
        return !string.IsNullOrWhiteSpace(displayName)
               && !string.Equals(displayName, ".", StringComparison.Ordinal)
               && !string.Equals(displayName, "..", StringComparison.Ordinal)
               && displayName.IndexOf('/') < 0
               && displayName.IndexOf('\\') < 0;
    }

    private static bool IsDirectChild(File parent, File child)
    {
        return string.Equals(
            child.ParentFile?.CanonicalPath,
            parent.CanonicalPath,
            StringComparison.Ordinal);
    }

    private bool DeleteRecursively(File file)
    {
        if (!IsWithinExternalStorage(file) || IsExternalStorageRoot(file)) return false;

        if (!file.IsDirectory) return file.Delete();
        
        var children = file.ListFiles();
        if (children is null) return false;

        return children.All(DeleteRecursively) && file.Delete();
    }

    private bool IsExternalStorageRoot(File file)
    {
        return string.Equals(
            file.CanonicalPath,
            Environment.ExternalStorageDirectory?.CanonicalPath,
            StringComparison.Ordinal);
    }

    private static bool IsWithinExternalStorage(File file)
    {
        var root = Environment.ExternalStorageDirectory;
        return root is not null && IsWithinExternalStorage(file, root.CanonicalPath);
    }

    private static bool IsWithinExternalStorage(File file, string rootPath)
    {
        var canonical = file.CanonicalPath;
        return string.Equals(canonical, rootPath, StringComparison.Ordinal)
               || canonical.StartsWith(rootPath.TrimEnd('/') + "/", StringComparison.Ordinal);
    }

    private static string GetMimeType(File file)
    {
        var extension = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
        var mimeType = string.IsNullOrWhiteSpace(extension)
            ? null
            : MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension);
        return string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
    }

    private static string AppendExtensionIfNeeded(string displayName, string? mimeType, bool isDirectory)
    {
        if (isDirectory
            || string.IsNullOrWhiteSpace(mimeType)
            || string.Equals(mimeType, "application/octet-stream", StringComparison.Ordinal))
            return displayName;

        var extension = MimeTypeMap.Singleton?.GetExtensionFromMimeType(mimeType);
        if (string.IsNullOrWhiteSpace(extension)
            || displayName.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
            return displayName;

        return displayName + "." + extension;
    }

    private static File EnsureUniqueTarget(File initial, bool isDirectory)
    {
        if (!initial.Exists()) return initial;

        var parent = initial.ParentFile;
        var name = initial.Name;
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);
        var baseName = string.IsNullOrEmpty(extension)
            ? name
            : name[..^extension.Length];

        for (var index = 1; index < 1000; index++)
        {
            var candidateName = string.IsNullOrEmpty(extension)
                ? $"{baseName} ({index})"
                : $"{baseName} ({index}){extension}";
            var candidate = new File(parent, candidateName);
            if (!candidate.Exists()) return candidate;
        }

        return initial;
    }

    private sealed class FileShuttleHandler(Looper looper, AgnosiaFileShuttleService service) : Handler(looper)
    {
        public override void HandleMessage(Message msg)
        {
            service.HandleRequest(msg);
        }
    }
}
