#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using Agnosia.Android.Services;
using Android.Content.PM;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryPackageStateCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryPackageState;

#if AGNOSIA_ANDROID
    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var operation = await HiddenAppSessionConcurrency.EnterOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ExecuteCoreAsync(envelope, context, cancellationToken).ConfigureAwait(false);
    }

    private Task<AndroidCommandResultEnvelope> ExecuteCoreAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        if (context.ActualProfile != AndroidCommandExecutionProfile.Work)
            return Task.FromResult(Failure(
                envelope,
                context,
                stopwatch,
                "Package state query must execute inside the work profile.",
                "profile_mismatch"));

        var ownerPackageName = context.Context.PackageName;
        if (context.PolicyManager is null
            || context.Admin is null
            || string.IsNullOrWhiteSpace(ownerPackageName)
            || !context.PolicyManager.IsProfileOwnerApp(ownerPackageName))
            return Task.FromResult(Failure(
                envelope,
                context,
                stopwatch,
                "Package state query requires the Agnosia profile owner.",
                "profile_owner_required"));

        PackageStateQuery? query;
        try
        {
            query = string.IsNullOrWhiteSpace(envelope.PayloadJson)
                ? null
                : JsonSerializer.Deserialize<PackageStateQuery>(envelope.PayloadJson);
        }
        catch (JsonException)
        {
            query = null;
        }

        if (query is null || string.IsNullOrWhiteSpace(query.PackageName))
            return Task.FromResult(Failure(
                envelope,
                context,
                stopwatch,
                "Package state query payload is invalid.",
                "package_state_payload_invalid"));

        try
        {
            var app = context.Context.PackageManager?.GetApplicationInfo(
                query.PackageName,
                AndroidSystemApi.GetInstalledApplicationFlags());
            var installed = app is not null
                            && (app.Flags & ApplicationInfoFlags.Installed) != 0;
            var hidden = installed
                         && context.PolicyManager.IsApplicationHidden(context.Admin, query.PackageName);
            var launchId = HiddenAppSessionMonitorService.GetPersistedLaunchId(query.PackageName);
            var recoveryAcknowledged = !string.IsNullOrWhiteSpace(query.ConfirmedRecoveryLaunchId)
                                       && HiddenAppSessionMonitorService.ConfirmParentNotification(
                                           query.PackageName,
                                           query.ConfirmedRecoveryLaunchId);
            var payloadJson = JsonSerializer.Serialize(new PackageStateResult(
                query.PackageName,
                installed,
                hidden,
                launchId,
                recoveryAcknowledged));

            stopwatch.Stop();
            return Task.FromResult(AndroidCommandResultEnvelope.Success(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                payloadJson,
                "Package state query completed.",
                stopwatch.Elapsed,
                $"package={query.PackageName}; installed={installed}; hidden={hidden}; actual={context.ActualProfile}"));
        }
        catch (PackageManager.NameNotFoundException)
        {
            stopwatch.Stop();
            var payloadJson = JsonSerializer.Serialize(new PackageStateResult(
                query.PackageName,
                false,
                false,
                HiddenAppSessionMonitorService.GetPersistedLaunchId(query.PackageName),
                false));
            return Task.FromResult(AndroidCommandResultEnvelope.Success(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                payloadJson,
                "Package state query completed.",
                stopwatch.Elapsed,
                $"package={query.PackageName}; installed=false; hidden=false; actual={context.ActualProfile}"));
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return Task.FromResult(Failure(
                envelope,
                context,
                stopwatch,
                "Android could not query the work package state.",
                "package_state_query_failed"));
        }
    }

    private static AndroidCommandResultEnvelope Failure(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        Stopwatch stopwatch,
        string message,
        string errorCode)
    {
        stopwatch.Stop();
        return AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            message,
            errorCode,
            stopwatch.Elapsed,
            $"requested={envelope.TargetProfile}; actual={context.ActualProfile}; contextSource={context.ContextSource}");
    }
#endif
}
