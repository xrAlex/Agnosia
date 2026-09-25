#if AGNOSIA_ANDROID
using System.Text.Json;
using Agnosia.Android.Permissions;
using Agnosia.Android.Services;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed record AppPermissionsRequest(string PackageName);

internal sealed class QueryAppPermissionsCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryAppPermissions;

#if AGNOSIA_ANDROID
    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<AppPermissionsRequest>(envelope.PayloadJson ?? "null");
        if (context.ActualProfile != AndroidCommandExecutionProfile.Work || context.PolicyManager is null
            || context.Admin is null || !context.PolicyManager.IsProfileOwnerApp(context.Context.PackageName!)
            || string.IsNullOrWhiteSpace(request?.PackageName))
            return AndroidCommandResultEnvelope.Failure(envelope.CorrelationId, envelope.Kind, context.Transport,
                "Не удалось прочитать разрешения рабочего приложения.", "invalid_profile_or_package", TimeSpan.Zero, "");

        using var operation = await HiddenAppSessionConcurrency.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var permissions = AndroidAppPermissionReader.Read(context.Context, request.PackageName, context.PolicyManager, context.Admin);
        return AndroidCommandResultEnvelope.Success(envelope.CorrelationId, envelope.Kind, context.Transport,
            JsonSerializer.Serialize(permissions), "Разрешения обновлены.", TimeSpan.Zero, "live; work-profile");
    }
#endif
}
