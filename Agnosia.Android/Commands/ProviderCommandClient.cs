using Android.Content;
using Android.OS;
using Uri = Android.Net.Uri;

namespace Agnosia.Android.Commands;

internal sealed class ProviderCommandClient(Context context)
{
    private readonly BoundedCommandExecutor _executor = new(2);

    public Task<ProviderCommandMessage> CallAsync(ProviderCommandMessage request, CancellationToken cancellationToken,
        CommandDispatchState? dispatch = null) => _executor.RunAsync(() =>
    {
        var key = AuthenticationUtility.GetExistingKey() ?? throw new InvalidOperationException("Authentication key is missing.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now >= request.Deadline) throw new TimeoutException("Command expired before dispatch.");
        request = request with { Timestamp = now };
        using var bundle = ToBundle(ProviderCommandProtocol.Sign(request, key));
        using var result = (dispatch ?? new CommandDispatchState()).Invoke(() => context.ContentResolver!.Call(
            Uri.Parse(ProviderCommandProtocol.AccessUri(request.TargetUserId))!,
            ProviderCommandProtocol.Method, null, bundle), cancellationToken);
        var response = FromBundle(result);
        if (!ProviderCommandProtocol.Verify(response, key) || !ProviderCommandProtocol.MatchesReply(request, response))
            throw new ProviderAuthenticationException();
        return response;
    }, cancellationToken, ordered: Enum.TryParse<AndroidCommandKind>(request.CommandKind, out var kind)
        && (ProviderCommandPolicy.IsMutation(kind) || kind == AndroidCommandKind.QueryPackageState));

    public static Bundle ToBundle(ProviderCommandMessage message)
    {
        var bundle = new Bundle();
        try
        {
            bundle.PutString(ProviderCommandProtocol.BundleKey, ProviderCommandProtocol.Serialize(message));
            CheckParcelSize(bundle);
            return bundle;
        }
        catch { bundle.Dispose(); throw; }
    }

    public static ProviderCommandMessage FromBundle(Bundle? bundle)
    {
        if (bundle is null) throw new IOException("Provider returned no response.");
        CheckParcelSize(bundle);
        return ProviderCommandProtocol.Deserialize(bundle.GetString(ProviderCommandProtocol.BundleKey)
            ?? throw new InvalidDataException("Provider message is missing."));
    }

    private static void CheckParcelSize(Bundle bundle)
    {
        using var parcel = Parcel.Obtain();
        parcel.WriteBundle(bundle);
        if (parcel.DataSize() > ProviderCommandProtocol.MaxMessageBytes)
            throw new InvalidDataException("Provider Parcel exceeds its budget.");
    }
}

internal sealed class ProviderAuthenticationException : Exception
{
    public ProviderAuthenticationException() : base("Provider response authentication failed.") { }
}
