using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Android;
using Android.Content;
using Android.Content.Res;
using Android.Database;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Files;

[ContentProvider(
    [AgnosiaFileShuttleContract.Authority],
    Name = AgnosiaFileShuttleContract.ProviderComponentName,
    Exported = true,
    Enabled = false,
    GrantUriPermissions = true,
    Permission = Manifest.Permission.ManageDocuments)]
[IntentFilter(["android.content.action.DOCUMENTS_PROVIDER"])]
public sealed class AgnosiaCrossProfileDocumentsProvider : DocumentsProvider
{
    private const string LogTag = "AgnosiaDocumentsProvider";

    private static readonly string[] DefaultRootProjection =
    [
        DocumentsContract.Root.ColumnRootId,
        DocumentsContract.Root.ColumnDocumentId,
        DocumentsContract.Root.ColumnIcon,
        DocumentsContract.Root.ColumnTitle,
        DocumentsContract.Root.ColumnFlags
    ];

    private static readonly string[] DefaultDocumentProjection =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnFlags,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified
    ];

    public override bool OnCreate()
    {
        if (Context is not null) AgnosiaRuntime.Initialize(Context);
        return true;
    }

    public override ICursor QueryRoots(string[]? projection)
    {
        var cursor = new MatrixCursor(projection ?? DefaultRootProjection);
        if (!IsProviderReady()) return cursor;

        var row = cursor.NewRow()
                  ?? throw new InvalidOperationException("Android did not create a DocumentsUI root row.");
        row.Add(DocumentsContract.Root.ColumnRootId, AgnosiaFileShuttleContract.DummyRoot);
        row.Add(DocumentsContract.Root.ColumnDocumentId, AgnosiaFileShuttleContract.DummyRoot);
        row.Add(DocumentsContract.Root.ColumnIcon, Resource.Mipmap.ic_launcher);
        row.Add(DocumentsContract.Root.ColumnTitle, GetRootTitle());
        row.Add(
            DocumentsContract.Root.ColumnFlags,
            (int)(DocumentRootFlags.SupportsCreate
                  | DocumentRootFlags.LocalOnly
                  | DocumentRootFlags.SupportsIsChild));
        return cursor;
    }

    public override ICursor QueryDocument(string? documentId, string[]? projection)
    {
        var cursor = new MatrixCursor(projection ?? DefaultDocumentProjection);
        if (string.IsNullOrWhiteSpace(documentId)) return cursor;

        try
        {
            if (string.Equals(documentId, AgnosiaFileShuttleContract.DummyRoot, StringComparison.Ordinal))
            {
                IncludeFile(cursor, CreateRootDocumentInfo());
                return cursor;
            }

            if (TryGetReadyClient(out var client)
                && client.LoadFileMeta(documentId) is { } info)
                IncludeFile(cursor, info);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query File Shuttle document {documentId}: {exception.Message}");
        }

        return cursor;
    }

    public override ICursor QueryChildDocuments(string? parentDocumentId, string[]? projection, string? sortOrder)
    {
        var cursor = new MatrixCursor(projection ?? DefaultDocumentProjection);
        if (string.IsNullOrWhiteSpace(parentDocumentId)) return cursor;

        try
        {
            cursor.SetNotificationUri(
                Context?.ContentResolver,
                DocumentsContract.BuildDocumentUri(AgnosiaFileShuttleContract.Authority, parentDocumentId));

            if (TryGetReadyClient(out var client))
                foreach (var info in client.LoadFiles(parentDocumentId))
                    IncludeFile(cursor, info);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query File Shuttle children for {parentDocumentId}: {exception.Message}");
        }

        return cursor;
    }

    public override ParcelFileDescriptor? OpenDocument(
        string? documentId,
        string? mode,
        CancellationSignal? signal)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return null;

        try
        {
            return TryGetReadyClient(out var client) ? client.OpenFile(documentId, mode ?? "r") : null;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to open File Shuttle document {documentId}: {exception.Message}");
            return null;
        }
    }

    public override AssetFileDescriptor? OpenDocumentThumbnail(
        string? documentId,
        Point? sizeHint,
        CancellationSignal? signal)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return null;

        try
        {
            if (!TryGetReadyClient(out var client)) return null;

            var descriptor = client.OpenThumbnail(documentId, sizeHint ?? new Point(128, 128));
            return descriptor is null
                ? null
                : new AssetFileDescriptor(descriptor, 0, AssetFileDescriptor.UnknownLength);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to open File Shuttle thumbnail {documentId}: {exception.Message}");
            return null;
        }
    }

    public override string? CreateDocument(string? parentDocumentId, string? mimeType, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(parentDocumentId) || string.IsNullOrWhiteSpace(displayName)) return null;

        try
        {
            if (!TryGetReadyClient(out var client)) return null;

            var documentId = client.CreateFile(
                parentDocumentId,
                mimeType ?? "application/octet-stream",
                displayName);
            if (!string.IsNullOrWhiteSpace(documentId)) NotifyDocumentChanged(parentDocumentId);
            return documentId;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to create File Shuttle document under {parentDocumentId}: {exception.Message}");
            return null;
        }
    }

    public override void DeleteDocument(string? documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return;

        try
        {
            if (!TryGetReadyClient(out var client)) return;

            var parentId = client.DeleteFile(documentId);
            if (!string.IsNullOrWhiteSpace(parentId)) NotifyDocumentChanged(parentId);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to delete File Shuttle document {documentId}: {exception.Message}");
        }
    }

    public override bool IsChildDocument(string? parentDocumentId, string? documentId)
    {
        if (string.IsNullOrWhiteSpace(parentDocumentId) || string.IsNullOrWhiteSpace(documentId)) return false;

        try
        {
            return TryGetReadyClient(out var client) && client.IsChildOf(parentDocumentId, documentId);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to check File Shuttle child document: {exception.Message}");
            return false;
        }
    }

    private bool IsProviderReady()
    {
        var context = Context;
        return context is not null
               && LocalStorageManager.Instance.GetBoolean(StorageKeys.CrossProfileFileShuttleEnabled)
               && AndroidPermissionApi.HasAllFilesAccess(context);
    }

    private AgnosiaFileShuttleMessengerClient GetClient()
    {
        if (Context is null) throw new InvalidOperationException("DocumentsProvider context is unavailable.");

        return AgnosiaFileShuttleClientBroker.GetClient(Context);
    }

    private bool TryGetReadyClient(out AgnosiaFileShuttleMessengerClient client)
    {
        if (IsProviderReady())
        {
            client = GetClient();
            if (!client.IsConnected)
                Log.Warn(LogTag, "File Shuttle provider has no preconnected bridge; manual Files launches are best-effort on Android 14+ because background activity starts can be blocked.");
            return true;
        }

        client = null!;
        return false;
    }

    private string GetRootTitle()
    {
        return Context is not null && AgnosiaUtilities.IsProfileOwner(Context)
            ? "Личный профиль"
            : "Рабочий профиль";
    }

    private void NotifyDocumentChanged(string documentId)
    {
        var uri = DocumentsContract.BuildDocumentUri(AgnosiaFileShuttleContract.Authority, documentId);
        if (uri is not null) Context?.ContentResolver?.NotifyChange(uri, null);
    }

    private static void IncludeFile(MatrixCursor cursor, AgnosiaFileShuttleDocumentInfo info)
    {
        var row = cursor.NewRow()
                  ?? throw new InvalidOperationException("Android did not create a DocumentsUI document row.");
        row.Add(DocumentsContract.Document.ColumnDocumentId, info.DocumentId);
        row.Add(DocumentsContract.Document.ColumnDisplayName, info.DisplayName);
        row.Add(DocumentsContract.Document.ColumnFlags, info.Flags);
        row.Add(DocumentsContract.Document.ColumnMimeType, info.MimeType);
        row.Add(DocumentsContract.Document.ColumnSize, info.Size);
        row.Add(DocumentsContract.Document.ColumnLastModified, info.LastModified);
    }

    private AgnosiaFileShuttleDocumentInfo CreateRootDocumentInfo()
    {
        return new AgnosiaFileShuttleDocumentInfo(
            AgnosiaFileShuttleContract.DummyRoot,
            GetRootTitle(),
            DocumentsContract.Document.MimeTypeDir,
            0,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            (int)DocumentContractFlags.DirSupportsCreate);
    }
}
