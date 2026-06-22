#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryCrossProfilePackagesCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryCrossProfilePackages;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        if (context.ActualProfile != AndroidCommandExecutionProfile.Work)
        {
            stopwatch.Stop();
            return Task.FromResult(AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                "Cross-profile package query must execute inside the work profile.",
                "profile_mismatch",
                stopwatch.Elapsed,
                $"requested={envelope.TargetProfile}; actual={context.ActualProfile}; contextSource={context.ContextSource}"));
        }

        if (context.PolicyManager is null || context.Admin is null)
        {
            stopwatch.Stop();
            return Task.FromResult(AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                "Рабочий профиль недоступен для чтения межпрофильных пакетов.",
                "profile_owner_unavailable",
                stopwatch.Elapsed,
                string.Empty));
        }

        var packages = AndroidPolicyApi.GetCrossProfilePackages(context.PolicyManager, context.Admin);
        stopwatch.Stop();
        return Task.FromResult(AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            JsonSerializer.Serialize(packages),
            "Cross-profile package query completed.",
            stopwatch.Elapsed,
            $"packageCount={packages.Length}; actualProfile={context.ActualProfile}; contextSource={context.ContextSource}"));
    }
#endif
}
