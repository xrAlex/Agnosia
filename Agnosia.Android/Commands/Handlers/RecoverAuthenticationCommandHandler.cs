#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using Agnosia.Android.Infrastructure;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class RecoverAuthenticationCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.RecoverAuthentication;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
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
                "Authentication recovery must execute inside the work profile.",
                "profile_mismatch"));

        var packageName = context.Context.PackageName;
        if (context.PolicyManager is null
            || string.IsNullOrWhiteSpace(packageName)
            || !context.PolicyManager.IsProfileOwnerApp(packageName))
            return Task.FromResult(Failure(
                envelope,
                context,
                stopwatch,
                "Authentication recovery requires the Agnosia profile owner.",
                "profile_owner_required"));

        AuthenticationRecoveryRequest? request;
        try
        {
            request = string.IsNullOrWhiteSpace(envelope.PayloadJson)
                ? null
                : JsonSerializer.Deserialize<AuthenticationRecoveryRequest>(envelope.PayloadJson);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
            return Task.FromResult(Failure(
                envelope,
                context,
                stopwatch,
                "Authentication recovery payload is invalid.",
                "authentication_recovery_payload_invalid"));

        if (!AuthenticationUtility.TryStoreProvisioningKey(request.ReplacementAuthKey))
            return Task.FromResult(Failure(
                envelope,
                context,
                stopwatch,
                "Replacement authentication key is invalid.",
                "authentication_key_invalid"));

        AndroidStartup.EnsureWorkProfilePoliciesAndStartLockFreezeMonitor(context.Context);
        return new ProfilePingCommandHandler().ExecuteAsync(envelope, context, cancellationToken);
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
            $"actual={context.ActualProfile}; contextSource={context.ContextSource}");
    }
#endif
}
