using System.Text.Json;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using Uri = Android.Net.Uri;

namespace Agnosia.Android.Commands;

internal sealed record CommandProviderAccess(int UserId, long Serial, string Generation);
internal sealed record CommandAccessResult(CommandProviderAccess? Access, string? Error)
{
    public bool Ready => Access is not null && Error is null;
}

internal sealed class CommandAccessCoordinator(Context context, LocalStorageManager storage,
    AndroidActivityCommandGateway gateway, ProviderCommandClient client)
{
    private const string AccessKey = "command_provider_access_v1";
    private readonly CommandSingleFlight<CommandAccessResult> _connection = new();
    private readonly Lock _sync = new();
    private CommandProviderAccess? _ready;
    private long? _bootstrapAttemptedSerial;
    private bool _unavailable;
    private bool _legacyProfileRoute;

    public bool IsConnecting { get; private set; }
    public bool UseLegacyProfileRoute { get { lock (_sync) return _legacyProfileRoute; } }

    public Task<CommandAccessResult> GetAccessAsync(CancellationToken token) =>
        _connection.RunAsync(() => PrepareAsync(false), token);

    public async Task<CommandAccessResult> PrepareInForegroundAsync(CancellationToken token)
    {
        var result = await _connection.RunAsync(() => PrepareAsync(true), token).ConfigureAwait(false);
        // A concurrent read may have checked a missing grant before the foreground request arrived.
        if (result.Error == "grant_missing")
            result = await _connection.RunAsync(() => PrepareAsync(true), token).ConfigureAwait(false);
        return result;
    }

    public void NotifyForegroundSession()
    {
        lock (_sync)
        {
            _unavailable = false;
        }
    }

    public void RetryOnExplicitRefresh()
    {
        lock (_sync)
        {
            _unavailable = false;
            if (!IsConnecting) _bootstrapAttemptedSerial = null;
        }
        AndroidQueryCache.Shared.ClearOwnerCheck();
    }

    public void NotifyProfileAvailabilityChanged()
    {
        lock (_sync) { _unavailable = false; _ready = null; }
        AndroidQueryCache.Shared.ClearOwnerCheck();
    }

    public void AllowForegroundRefresh()
    {
        lock (_sync) _unavailable = false;
    }

    public void NotifyWorkProfileAppUpdated()
    {
        lock (_sync)
        {
            _ready = null;
            _unavailable = false;
            _legacyProfileRoute = false;
            _bootstrapAttemptedSerial = null;
        }
    }

    public void Invalidate(string error)
    {
        lock (_sync)
        {
            _ready = null;
            if (error == "profile_unavailable") _unavailable = true;
            if (error is "grant_missing" or "wrong_profile")
            {
                storage.Remove(AccessKey);
                _bootstrapAttemptedSerial = null;
            }
        }
    }

    private async Task<CommandAccessResult> PrepareAsync(bool allowBootstrap)
    {
        if (!ProviderTransportOptions.Enabled) return new(null, "provider_disabled");
        // Background commands must leave time for the Activity fallback in Auto mode.
        // Foreground bootstrap may need longer because Android can start an Activity.
        using var timeout = new CancellationTokenSource(
            allowBootstrap ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(7));
        try
        {
            var key = AuthenticationUtility.GetExistingKey();
            if (string.IsNullOrWhiteSpace(key)) return new(null, "authentication_key_missing");
            var manager = AndroidSystemApi.GetUserManager(context);
            var profiles = manager?.UserProfiles?.Where(user => ProviderProfileIdentity.UserId(user) != ProviderProfileIdentity.CurrentUserId).ToArray() ?? [];
            var expectedSerial = storage.GetLong(StorageKeys.ManagedProfileUserSerial, -1);
            var target = expectedSerial >= 0
                ? profiles.FirstOrDefault(user => manager!.GetSerialNumberForUser(user) == expectedSerial)
                : profiles.Length == 1 ? profiles[0] : null;
            if (target is null) { Invalidate("wrong_profile"); return new(null, "profile_unavailable"); }
            var serial = manager!.GetSerialNumberForUser(target);
            var userId = ProviderProfileIdentity.UserId(target);
            if (serial < 0 || manager.IsQuietModeEnabled(target)) return new(null, "profile_unavailable");
            lock (_sync) { if (_unavailable) return new(null, "profile_unavailable"); }

            CommandProviderAccess? access;
            lock (_sync) access = _ready;
            if (access is not null && access.UserId == userId && access.Serial == serial && HasGrant(userId)) return new(access, null);
            if (access is not null && !HasGrant(userId))
                lock (_sync) _bootstrapAttemptedSerial = null;
            access = ReadAccess();
            if (access?.UserId != userId || access.Serial != serial || !HasGrant(userId)) access = null;
            if (access is null)
            {
                lock (_sync) _ready = null;
                storage.Remove(AccessKey);
                if (!allowBootstrap || !MainActivity.CanPrepareCommandAccess) return new(null, "grant_missing");
                lock (_sync)
                {
                    if (_bootstrapAttemptedSerial == serial) return new(null, "grant_missing");
                    _bootstrapAttemptedSerial = serial;
                    IsConnecting = true;
                }
                try { access = await BootstrapAsync(userId, serial, timeout.Token).ConfigureAwait(false); }
                finally { lock (_sync) IsConnecting = false; }
                if (access is null) return new(null, UseLegacyProfileRoute ? "legacy_profile" : "grant_missing");
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var ping = new ProviderCommandMessage(ProviderCommandProtocol.Version, Guid.NewGuid(), nameof(AndroidCommandKind.ProfilePing),
                ProviderProfileIdentity.CurrentUserId, access.UserId, access.Generation, now, now + 10_000, false, false, null, null, "", "");
            var response = await client.CallAsync(ping, timeout.Token).ConfigureAwait(false);
            if (!response.Succeeded)
            {
                if (response.ErrorCode == "wrong_profile") Invalidate("wrong_profile");
                return new(null, response.ErrorCode ?? "provider_unavailable");
            }
            lock (_sync) { _ready = access; _legacyProfileRoute = false; }
            storage.SetStringDurably(AccessKey, JsonSerializer.Serialize(access));
            Log.Info("AgnosiaCommandAccess", $"Provider ready. user={userId}; protocol={response.ProtocolVersion}.");
            return new(access, null);
        }
        catch (ProviderAuthenticationException) { return new(null, "authentication_failed"); }
        catch (Java.Lang.SecurityException)
        {
            var access = ReadAccess();
            if (access is not null && HasGrant(access.UserId)) return new(null, "authentication_failed");
            // Keep the failed attempt marker: an error while accepting a new grant must not cause a resume loop.
            lock (_sync) _ready = null;
            storage.Remove(AccessKey);
            return new(null, "grant_missing");
        }
        catch (Exception exception)
        {
            Log.Warn("AgnosiaCommandAccess", $"Provider connection failed: {exception.GetType().Name}.");
            Invalidate("profile_unavailable");
            return new(null, "profile_unavailable");
        }
    }

    private bool HasGrant(int userId) => context.ContentResolver?.PersistedUriPermissions?.Any(permission =>
        // Android stores the source user separately and exposes the persisted URI without its user prefix.
        // The signed ping below still binds this candidate grant to the selected profile and generation.
        permission.IsWritePermission && ProviderCommandProtocol.IsAccessUri(permission.Uri?.ToString(), userId)) == true;

    private CommandProviderAccess? ReadAccess()
    {
        try { return JsonSerializer.Deserialize<CommandProviderAccess>(storage.GetString(AccessKey) ?? "null"); }
        catch (JsonException) { storage.Remove(AccessKey); return null; }
    }

    private async Task<CommandProviderAccess?> BootstrapAsync(int userId, long serial, CancellationToken token)
    {
        // Existing installations already registered ProfilePing in DPM. Carry the signed bootstrap
        // marker on that action so an APK upgrade needs no preliminary policy-refresh Activity.
        using var intent = new Intent(AgnosiaActions.ProfilePing);
        intent.AddFlags(ActivityFlags.NoAnimation);
        intent.PutExtra(ProviderCommandProtocol.BootstrapExtra, true);
        intent.PutExtra(ProviderCommandProtocol.SourceUserExtra, ProviderProfileIdentity.CurrentUserId);
        intent.PutExtra(ProviderCommandProtocol.TargetUserExtra, userId);
        var result = await gateway.StartActivityForResultAsync(intent, true, token).ConfigureAwait(false);
        if (result.ResultCode != Result.Ok || result.Data is not { } data) return null;
        // The gateway verified the result's signature/command identity. Data and flags are checked separately.
        var proofJson = data.GetStringExtra(ProviderCommandProtocol.BootstrapExtra);
        if (proofJson is null && data.GetBooleanExtra(AndroidCommandContract.ResultProfileOwnerCheckPerformed, false)
            && data.GetBooleanExtra(AndroidCommandContract.ResultIsProfileOwner, false))
        {
            lock (_sync) _legacyProfileRoute = true;
            Log.Info("AgnosiaCommandAccess", "Signed legacy ProfilePing received; selecting compatibility mode before command dispatch.");
            return null;
        }
        var signed = ProviderCommandProtocol.Deserialize(proofJson ?? "");
        var expectedFlags = ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission;
        if (signed.ProtocolVersion != ProviderCommandProtocol.Version || signed.SourceUserId != userId
            || signed.TargetUserId != ProviderProfileIdentity.CurrentUserId || string.IsNullOrWhiteSpace(signed.ProfileGeneration)
            || signed.CommandKind != nameof(AndroidCommandKind.ConnectCommandProvider) || !signed.IsResponse || !signed.Succeeded
            || signed.PayloadJson != $"{ProviderCommandProtocol.Authority}{ProviderCommandProtocol.Path}:write:persistable"
            || (data.Flags & expectedFlags) != expectedFlags || !ProviderCommandProtocol.IsAccessUri(data.Data?.ToString(), userId))
            throw new ProviderAuthenticationException();
        var uri = Uri.Parse(ProviderCommandProtocol.AccessUri(userId))!;
        context.ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantWriteUriPermission);
        return HasGrant(userId) ? new(userId, serial, signed.ProfileGeneration) : null;
    }
}
