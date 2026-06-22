#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryPermissionsCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryPermissions;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var usageStatsAccess = AndroidUsageStatsAccessApi.HasAccess(
            context.Context,
            "AgnosiaCommandPermissions");
        var packageInstallAccess = AndroidPackageApi.CanRequestInstalls(
            context.Context,
            "AgnosiaCommandPermissions");
        var allFilesAccess = AndroidPermissionApi.HasAllFilesAccess(context.Context);
        var payloadJson = JsonSerializer.Serialize(new QueryPermissionsPayload(
            usageStatsAccess,
            packageInstallAccess,
            allFilesAccess));

        stopwatch.Stop();
        return Task.FromResult(AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            payloadJson,
            "Permission access query completed.",
            stopwatch.Elapsed,
            $"usageStatsAccess={usageStatsAccess}; packageInstallAccess={packageInstallAccess}; allFilesAccess={allFilesAccess}"));
    }

    private sealed record QueryPermissionsPayload(
        [property: JsonPropertyName(AndroidCommandContract.ResultUsageStatsAccess)]
        bool UsageStatsAccess,
        [property: JsonPropertyName(AndroidCommandContract.ResultPackageInstallAccess)]
        bool PackageInstallAccess,
        [property: JsonPropertyName(AndroidCommandContract.ResultAllFilesAccess)]
        bool AllFilesAccess);
#endif
}
