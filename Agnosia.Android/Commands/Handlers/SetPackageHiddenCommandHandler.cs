using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;
#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using Agnosia.Android.Services;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed record SetPackageHiddenRequest([property: JsonPropertyName(AndroidCommandContract.ExtraPackage)] string PackageName);

internal sealed class SetPackageHiddenCommandHandler(bool hidden) : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => hidden ? AndroidCommandKind.FreezePackage : AndroidCommandKind.UnfreezePackage;

#if AGNOSIA_ANDROID
    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var request = JsonSerializer.Deserialize<SetPackageHiddenRequest>(envelope.PayloadJson ?? "null");
        var package = request?.PackageName;
        if (context.ActualProfile != AndroidCommandExecutionProfile.Work || context.PolicyManager is null
            || context.Admin is null || string.IsNullOrWhiteSpace(package))
            return Failure("Не удалось определить приложение рабочего профиля.", "invalid_request");

        using var operation = await HiddenAppSessionConcurrency.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (hidden && AndroidWorkProfilePackageClassifier.IsSystemPackage(context.Context.PackageManager, package))
            return Success("Системные приложения рабочего профиля не замораживаются Agnosia.");

        const string tag = "AgnosiaPackageHidden";
        if (!hidden && HiddenAppSessionMonitorService.GetPackagesAwaitingHide().Contains(package))
        {
            if (!AndroidPolicyApi.TrySetApplicationHidden(context.PolicyManager, context.Admin, package, true, tag, out var freezeError))
                return Failure(freezeError ?? "Не удалось завершить временную сессию.", "package_policy_failed");
            HiddenAppSessionMonitorService.CompletePackage(context.Context, package);
        }
        if (!AndroidPolicyApi.TrySetApplicationHidden(context.PolicyManager, context.Admin, package, hidden, tag, out var error))
            return Failure(error ?? "Не удалось изменить видимость приложения.", "package_policy_failed");
        if (hidden) HiddenAppSessionMonitorService.CompletePackage(context.Context, package);
        AndroidQueryCache.Shared.ClearAppInventoryQueries();
        return Success(hidden ? "Приложение скрыто." : "Приложение снова доступно в рабочем профиле.");

        AndroidCommandResultEnvelope Success(string message) => AndroidCommandResultEnvelope.Success(envelope.CorrelationId,
            envelope.Kind, context.Transport, null, message, stopwatch.Elapsed, "desired-state; session-serialized");
        AndroidCommandResultEnvelope Failure(string message, string errorCode) => AndroidCommandResultEnvelope.Failure(envelope.CorrelationId,
            envelope.Kind, context.Transport, message, errorCode, stopwatch.Elapsed, "desired-state; session-serialized");
    }
#endif
}
