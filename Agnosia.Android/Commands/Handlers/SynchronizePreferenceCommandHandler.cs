using Agnosia.Android.Storage;

#if AGNOSIA_ANDROID
using System.Text.Json;
using Agnosia.Android.Infrastructure;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class SynchronizePreferenceCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.SynchronizePreference;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope, AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.ActualProfile != AndroidCommandExecutionProfile.Work
            || string.IsNullOrWhiteSpace(envelope.PayloadJson))
            return Task.FromResult(Failure("invalid_request"));

        using var document = JsonDocument.Parse(envelope.PayloadJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("Name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("Boolean", out var booleanElement)
            || booleanElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            return Task.FromResult(Failure("invalid_request"));

        var name = nameElement.GetString();
        if (name is null || !SettingsManager.SynchronizedNames.Contains(name, StringComparer.Ordinal))
            return Task.FromResult(Failure("invalid_request"));

        var value = booleanElement.GetBoolean();
        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetValues(
            new Dictionary<string, bool> { [name] = value },
            new Dictionary<string, string>());
        if (name == StorageKeys.LoggingEnabled && !value)
            AndroidAppLogArchive.Clear(context.Context);
        AndroidStartup.EnforceWorkProfilePolicies(context.Context);
        return Task.FromResult(AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId, envelope.Kind, context.Transport, null,
            "Настройка применена в рабочем профиле.", TimeSpan.Zero, "preferenceSynchronized=true"));

        AndroidCommandResultEnvelope Failure(string errorCode) => AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId, envelope.Kind, context.Transport,
            "Не удалось применить настройку в рабочем профиле.", errorCode, TimeSpan.Zero, "invalidPreferenceRequest=true");
    }
#endif
}
