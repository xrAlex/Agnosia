using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agnosia.Android.Api.Commands;

public sealed record ProviderCommandMessage(int ProtocolVersion, Guid CorrelationId, string CommandKind,
    int SourceUserId, int TargetUserId, string ProfileGeneration, long Timestamp, long Deadline,
    bool IsResponse, bool Succeeded, string? PayloadJson, string? ErrorCode, string Message, string Signature);

public static class ProviderCommandProtocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 256 * 1024;
    // Nested JSON is escaped again in the wire envelope and stored as UTF-16 in a Bundle.
    public const int MaxInventoryJsonBytes = 24 * 1024;
    public const int MaxIcons = 24;
    public const string Authority = "com.agnosia.app.commands";
    public const string Path = "/commands";
    public const string Method = "execute.v1";
    public const string BundleKey = "command";
    public const string BootstrapExtra = "agnosia.provider.access";
    public const string SourceUserExtra = "agnosia.provider.source_user";
    public const string TargetUserExtra = "agnosia.provider.target_user";

    public static string AccessUri(int userId) => $"content://{userId}@{Authority}{Path}";

    public static bool IsAccessUri(string? uri, int userId) => userId >= 0 &&
        (string.Equals(uri, AccessUri(userId), StringComparison.Ordinal)
         || string.Equals(uri, $"content://{Authority}{Path}", StringComparison.Ordinal));

    public static ProviderCommandMessage Sign(ProviderCommandMessage message, string key) =>
        message with { Signature = Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(key), Transcript(message))) };

    public static bool Verify(ProviderCommandMessage message, string key)
    {
        try
        {
            var signature = Convert.FromHexString(message.Signature);
            return signature.Length == 32 && CryptographicOperations.FixedTimeEquals(signature,
                HMACSHA256.HashData(Convert.FromHexString(key), Transcript(message)));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException) { return false; }
    }

    public static string Fingerprint(ProviderCommandMessage message) => Convert.ToHexString(SHA256.HashData(Transcript(message)));

    public static string Serialize(ProviderCommandMessage message)
    {
        var json = JsonSerializer.Serialize(message, ProviderWireJsonContext.Default.ProviderCommandMessage);
        CheckSize(json);
        return json;
    }

    public static ProviderCommandMessage Deserialize(string json)
    {
        CheckSize(json);
        return JsonSerializer.Deserialize(json, ProviderWireJsonContext.Default.ProviderCommandMessage)
               ?? throw new InvalidDataException("Missing provider message.");
    }

    public static string? ValidateRequest(ProviderCommandMessage message, int callerUser, int targetUser,
        string generation, long now)
    {
        if (message.ProtocolVersion != Version) return "incompatible_version";
        if (message.IsResponse || message.CorrelationId == Guid.Empty || string.IsNullOrWhiteSpace(message.CommandKind)
            || message.Succeeded || message.ErrorCode is not null || message.Deadline < message.Timestamp
            || message.Deadline - message.Timestamp > 120_000) return "invalid_request";
        if (message.SourceUserId != callerUser || message.TargetUserId != targetUser || callerUser == targetUser
            || string.IsNullOrWhiteSpace(generation) || message.ProfileGeneration != generation) return "wrong_profile";
        if (now < message.Timestamp || now - message.Timestamp > 30_000 || now >= message.Deadline) return "deadline_exceeded";
        return null;
    }

    public static ProviderCommandMessage Reply(ProviderCommandMessage request, bool succeeded, string? payload,
        string? error, string message) => request with
    {
        SourceUserId = request.TargetUserId, TargetUserId = request.SourceUserId, IsResponse = true,
        Succeeded = succeeded, PayloadJson = payload, ErrorCode = error, Message = message, Signature = ""
    };

    public static bool MatchesReply(ProviderCommandMessage request, ProviderCommandMessage response) =>
        response.IsResponse && response.ProtocolVersion == Version && response.CorrelationId == request.CorrelationId
        && response.CommandKind == request.CommandKind && response.SourceUserId == request.TargetUserId
        && response.TargetUserId == request.SourceUserId && response.ProfileGeneration == request.ProfileGeneration
        && response.Timestamp == request.Timestamp && response.Deadline == request.Deadline;

    private static byte[] Transcript(ProviderCommandMessage message) => Encoding.UTF8.GetBytes(
        "AGNOSIA_PROVIDER_1\n" + JsonSerializer.Serialize(message with { Signature = "" }, ProviderWireJsonContext.Default.ProviderCommandMessage));

    private static void CheckSize(string json)
    {
        if (json.Length > (MaxMessageBytes - 4096) / 2 || Encoding.UTF8.GetByteCount(json) > MaxMessageBytes - 4096)
            throw new InvalidDataException("Provider message exceeds the wire budget.");
    }
}

[JsonSerializable(typeof(ProviderCommandMessage))]
internal partial class ProviderWireJsonContext : JsonSerializerContext;
