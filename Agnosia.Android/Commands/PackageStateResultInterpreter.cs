using System.Text.Json;
using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal readonly record struct PackageStateSnapshot(
    OperationResult Result,
    bool Installed,
    bool Hidden,
    string? LaunchId,
    bool RecoveryAcknowledged);

internal static class PackageStateResultInterpreter
{
    public static OperationResult Interpret(
        AndroidCommandResultEnvelope result,
        string expectedPackageName,
        bool expectedHidden)
    {
        var snapshot = ReadSnapshot(result, expectedPackageName);
        if (!snapshot.Result.Succeeded) return snapshot.Result;

        if (!snapshot.Installed)
            return OperationResult.Failure(
                "Рабочая копия приложения не установлена.");

        return snapshot.Hidden == expectedHidden
            ? OperationResult.Success("Рабочая копия приложения подтверждена.")
            : OperationResult.Failure(expectedHidden
                ? "Рабочая копия приложения установлена, но не скрыта."
                : "Системная рабочая копия неожиданно скрыта.");
    }

    public static PackageStateSnapshot ReadSnapshot(
        AndroidCommandResultEnvelope result,
        string expectedPackageName)
    {
        if (!result.Succeeded)
            return Failure(string.IsNullOrWhiteSpace(result.Message)
                ? "Рабочий профиль не подтвердил состояние приложения."
                : result.Message);

        if (result.Transport is not (AndroidCommandTransportKind.Activity or AndroidCommandTransportKind.Provider))
            return Failure("Состояние рабочей копии получено по недоверенному каналу.");

        PackageStateResult? state;
        try
        {
            state = string.IsNullOrWhiteSpace(result.PayloadJson)
                ? null
                : JsonSerializer.Deserialize<PackageStateResult>(result.PayloadJson);
        }
        catch (JsonException)
        {
            state = null;
        }

        if (state is null
            || !string.Equals(state.PackageName, expectedPackageName, StringComparison.Ordinal))
            return Failure("Рабочий профиль вернул состояние другого приложения.");

        return new PackageStateSnapshot(
            OperationResult.Success("Рабочий профиль вернул состояние приложения."),
            state.Installed,
            state.Hidden,
            state.LaunchId,
            state.RecoveryAcknowledged);
    }

    private static PackageStateSnapshot Failure(string message)
    {
        return new PackageStateSnapshot(
            OperationResult.Failure(message),
            false,
            false,
            null,
            false);
    }
}
