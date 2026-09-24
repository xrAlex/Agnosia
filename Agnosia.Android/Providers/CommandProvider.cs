using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.OS;
using Agnosia.Android.Commands.Handlers;
using System.Text.Json;
using Process = Android.OS.Process;
using Uri = Android.Net.Uri;
using OperationCanceledException = System.OperationCanceledException;

namespace Agnosia.Android.Providers;

[ContentProvider([ProviderCommandProtocol.Authority], Name = "com.agnosia.app.CommandProvider", Exported = false)]
[GrantUriPermission(Path = ProviderCommandProtocol.Path)]
public sealed class CommandProvider : ContentProvider
{
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly ProviderReplayStore _replays = new();

    public override bool OnCreate() => true;

    public override Bundle? Call(string method, string? arg, Bundle? extras)
    {
        var context = Context ?? throw new InvalidOperationException("Provider has no context.");
        // Capture and authorize the actual Binder caller before clearing identity or starting workers.
        var uid = Binder.CallingUid;
        var pid = Binder.CallingPid;
        var callerUser = ProviderProfileIdentity.UserId(UserHandle.GetUserHandleForUid(uid));
        if (method != ProviderCommandProtocol.Method || arg is not null
            || !ProviderCallerIdentity.IsSameApp(uid, Process.MyUid()))
            throw new Java.Lang.SecurityException("Unauthorized provider caller.");

        // Call() does not enforce per-URI write permissions. A retained Binder must stop
        // authorizing commands immediately after its persisted URI grant is revoked.
        if (context.CheckUriPermission(Uri.Parse(ProviderCommandProtocol.AccessUri(ProviderProfileIdentity.CurrentUserId))!,
                pid, uid, ActivityFlags.GrantWriteUriPermission) != Permission.Granted)
            throw new Java.Lang.SecurityException("Command URI write access is missing.");

        // Runtime/storage initialization is idempotent and uses our own identity even on a cold start.
        var initializationIdentity = Binder.ClearCallingIdentity();
        try { AgnosiaRuntime.Initialize(context); }
        finally { Binder.RestoreCallingIdentity(initializationIdentity); }
        var key = AuthenticationUtility.GetExistingKey();
        var request = ProviderCommandClient.FromBundle(extras);
        if (key is null || !ProviderCommandProtocol.Verify(request, key) || request.SourceUserId != callerUser)
            throw new Java.Lang.SecurityException("Invalid command authentication.");

        var identity = Binder.ClearCallingIdentity();
        try
        {
            if (!ProviderProfileIdentity.IsSibling(context, callerUser) || !AgnosiaUtilities.IsProfileOwner(context))
                throw new Java.Lang.SecurityException("Invalid command authentication.");

            var error = ProviderCommandProtocol.ValidateRequest(request, callerUser, ProviderProfileIdentity.CurrentUserId,
                ProviderProfileIdentity.Generation(context, key), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (!Enum.TryParse<AndroidCommandKind>(request.CommandKind, out var kind) || !ProviderCommandPolicy.Supports(kind))
                error ??= "unsupported_command";
            if (AndroidSystemApi.GetUserManager(context)?.IsUserUnlocked != true) error = "profile_unavailable";
            if (error is not null) return Reply(request, false, null, error, "Provider rejected the command.", key);
            if (!_slots.Wait(0)) return Reply(request, false, null, "provider_busy", "Provider is busy.", key);
            try
            {
                var response = _replays.ExecuteAsync(request,
                    () => ExecuteAsync(context, request, kind), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).GetAwaiter().GetResult();
                try { return ProviderCommandClient.ToBundle(ProviderCommandProtocol.Sign(response, key)); }
                catch (InvalidDataException) { return Reply(request, false, null, "payload_too_large", "Provider response exceeds its budget.", key); }
            }
            finally { _slots.Release(); }
        }
        finally { Binder.RestoreCallingIdentity(identity); }
    }

    private static async Task<ProviderCommandMessage> ExecuteAsync(Context context, ProviderCommandMessage request, AndroidCommandKind kind)
    {
        var remaining = request.Deadline - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remaining <= 0) return ProviderCommandProtocol.Reply(request, false, null, "deadline_exceeded", "Command expired.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(remaining));
        try
        {
            var payload = request.PayloadJson;
            if (kind == AndroidCommandKind.QueryApps)
            {
                var query = JsonSerializer.Deserialize<QueryAppsRequest>(payload ?? "null") ?? QueryAppsRequest.Empty;
                payload = JsonSerializer.Serialize(query with { Limit = Math.Clamp(query.Limit <= 0 ? 100 : query.Limit, 1, 100),
                    MaxJsonBytes = Math.Min(query.MaxJsonBytes <= 0 ? ProviderCommandProtocol.MaxInventoryJsonBytes : query.MaxJsonBytes,
                        ProviderCommandProtocol.MaxInventoryJsonBytes) });
            }
            if (kind == AndroidCommandKind.QueryAppIcons
                && (JsonSerializer.Deserialize<QueryAppIconsRequest>(payload ?? "null")?.PackageNames?.Length ?? 0) > ProviderCommandProtocol.MaxIcons)
                return ProviderCommandProtocol.Reply(request, false, null, "payload_too_large", "Too many icons.");
            var envelope = new AndroidCommandEnvelope(request.CorrelationId, kind, AndroidCommandTargetProfile.Work,
                AndroidCommandInteractivity.NonInteractive, ProviderCommandPolicy.IsMutation(kind)
                    ? AndroidCommandPriority.Mutation : AndroidCommandPriority.UserBlocking, TimeSpan.FromMilliseconds(remaining), payload);
            var executionContext = ServiceRegistry.GetRequiredService<AndroidCommandExecutionContextFactory>()
                .Create(context, null, envelope, AndroidCommandTransportKind.Provider, "provider");
            var result = await ServiceRegistry.GetRequiredService<AndroidCommandHandlerExecutor>()
                .ExecuteAsync(envelope, executionContext, deadline.Token).ConfigureAwait(false);
            var response = ProviderCommandProtocol.Reply(request, result.Succeeded, result.PayloadJson, result.ErrorCode, result.Message);
            // Check before caching: an oversized reply must not occupy the replay window's memory.
            try { ProviderCommandProtocol.Serialize(response with { Signature = new string('0', 64) }); }
            catch (InvalidDataException) { return ProviderCommandProtocol.Reply(request, false, null, "payload_too_large", "Provider response exceeds its budget."); }
            return response;
        }
        catch (OperationCanceledException) { return ProviderCommandProtocol.Reply(request, false, null,
            ProviderCommandPolicy.IsMutation(kind) ? "outcome_unknown" : "deadline_exceeded", "Command deadline exceeded."); }
        catch (Exception) { return ProviderCommandProtocol.Reply(request, false, null,
            ProviderCommandPolicy.IsMutation(kind) ? "outcome_unknown" : "handler_failed", "Provider command failed."); }
    }

    private static Bundle Reply(ProviderCommandMessage request, bool success, string? payload, string error, string message, string key) =>
        ProviderCommandClient.ToBundle(ProviderCommandProtocol.Sign(ProviderCommandProtocol.Reply(request, success, payload, error, message), key));

    public override ICursor? Query(Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => throw new Java.Lang.UnsupportedOperationException();
    public override string? GetType(Uri uri) => null;
    public override Uri? Insert(Uri uri, ContentValues? values) => throw new Java.Lang.UnsupportedOperationException();
    public override int Delete(Uri uri, string? selection, string[]? selectionArgs) => throw new Java.Lang.UnsupportedOperationException();
    public override int Update(Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => throw new Java.Lang.UnsupportedOperationException();
}
