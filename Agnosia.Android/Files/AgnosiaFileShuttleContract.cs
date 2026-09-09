using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;

namespace Agnosia.Android.Files;

internal static class AgnosiaFileShuttleContract
{
    public const string ExtraConnectionId = "fileShuttleConnectionId";
    public const string Authority = "com.agnosia.app.documents";
    public const string ProviderComponentName = AndroidCommandContract.FileShuttleDocumentsProviderComponent;
    public const string DummyRoot = "/agnosia_storage_root/";

    public const int MessageConnectResult = 1;
    public const int MessageDisconnected = 2;
    public const int MessageUnregisterClient = 3;
    public const int MessageLoadFileMeta = 10;
    public const int MessageLoadFiles = 11;
    public const int MessageOpenFile = 12;
    public const int MessageOpenThumbnail = 13;
    public const int MessageCreateFile = 14;
    public const int MessageDeleteFile = 15;
    public const int MessageIsChildOf = 16;
    public const int MessageLoadAvailableBytes = 17;

    public const string ExtraRequestId = "request_id";
    public const string ExtraPath = "path";
    public const string ExtraParentPath = "parent_path";
    public const string ExtraChildPath = "child_path";
    public const string ExtraMode = "mode";
    public const string ExtraMimeType = "mime_type";
    public const string ExtraDisplayName = "display_name";
    public const string ExtraWidth = "width";
    public const string ExtraHeight = "height";
    public const string ExtraError = "error";
    public const string ExtraServiceMessenger = "service_messenger";
    public const string ExtraReconnectIntent = "reconnect_intent";
    public const string ExtraFileInfoJson = "file_info_json";
    public const string ExtraFileListJson = "file_list_json";
    public const string ExtraPageOffset = "page_offset";
    public const string ExtraPageToken = "page_token";
    public const string ExtraNextPageOffset = "next_page_offset";
    public const string ExtraHasMore = "has_more";
    public const string ExtraFileDescriptor = "file_descriptor";
    public const string ExtraCreatedDocumentId = "created_document_id";
    public const string ExtraDeletedParentId = "deleted_parent_id";
    public const string ExtraIsChild = "is_child";
    public const string ExtraAvailableBytes = "available_bytes";
}

internal sealed record AgnosiaFileShuttleDocumentInfo(
    string DocumentId,
    string DisplayName,
    string MimeType,
    long Size,
    long LastModified,
    int Flags);

[JsonSerializable(typeof(AgnosiaFileShuttleDocumentInfo))]
[JsonSerializable(typeof(AgnosiaFileShuttleDocumentInfo[]))]
[JsonSerializable(typeof(List<AgnosiaFileShuttleDocumentInfo>))]
internal sealed partial class AgnosiaFileShuttleJsonContext : JsonSerializerContext;
