#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Android.Receivers;
using Android.Content.PM;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class ProfilePingCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.ProfilePing;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var isProfileOwner = IsProfileOwner(context);
        var (versionCode, versionName) = ReadAppVersion(context);
        var payloadJson = JsonSerializer.Serialize(new ProfilePingPayload(
            true,
            isProfileOwner,
            versionCode,
            versionName));

        stopwatch.Stop();
        return Task.FromResult(AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            payloadJson,
            isProfileOwner
                ? "Profile owner check succeeded."
                : "Profile owner check completed; Agnosia is not profile owner.",
            stopwatch.Elapsed,
            $"profileOwner={isProfileOwner}; versionCode={versionCode}; versionName={versionName ?? "<null>"}"));
    }

    private static bool IsProfileOwner(AndroidCommandExecutionContext context)
    {
        var packageName = context.Context.PackageName;
        return !string.IsNullOrWhiteSpace(packageName)
               && context.PolicyManager?.IsProfileOwnerApp(packageName) == true;
    }

    private static (long VersionCode, string? VersionName) ReadAppVersion(AndroidCommandExecutionContext context)
    {
        try
        {
            var packageName = context.Context.PackageName;
            if (string.IsNullOrWhiteSpace(packageName))
                return (0, null);

            var packageInfo = context.Context.PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.MatchAll);
            return (packageInfo?.LongVersionCode ?? 0, packageInfo?.VersionName);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return (0, null);
        }
    }

    private sealed record ProfilePingPayload(
        [property: JsonPropertyName(AndroidCommandContract.ResultProfileOwnerCheckPerformed)]
        bool ProfileOwnerCheckPerformed,
        [property: JsonPropertyName(AndroidCommandContract.ResultIsProfileOwner)]
        bool IsProfileOwner,
        [property: JsonPropertyName(AndroidCommandContract.ResultAppVersionCode)]
        long AppVersionCode,
        [property: JsonPropertyName(AndroidCommandContract.ResultAppVersionName)]
        string? AppVersionName);
#endif
}
