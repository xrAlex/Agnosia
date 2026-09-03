using System.Text.Json;
using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal static class PackageStateResultInterpreter
{
    public static OperationResult Interpret(
        AndroidCommandResultEnvelope result,
        string expectedPackageName,
        bool expectedHidden)
    {
        if (!result.Succeeded)
            return OperationResult.Failure(string.IsNullOrWhiteSpace(result.Message)
                ? "Рабочий профиль не подтвердил состояние приложения."
                : result.Message);

        if (result.Transport != AndroidCommandTransportKind.Activity)
            return OperationResult.Failure(
                "Состояние рабочей копии получено по недоверенному каналу.");

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
            return OperationResult.Failure(
                "Рабочий профиль вернул состояние другого приложения.");

        if (!state.Installed)
            return OperationResult.Failure(
                "Рабочая копия приложения не установлена.");

        return state.Hidden == expectedHidden
            ? OperationResult.Success("Рабочая копия приложения подтверждена.")
            : OperationResult.Failure(expectedHidden
                ? "Рабочая копия приложения установлена, но не скрыта."
                : "Системная рабочая копия неожиданно скрыта.");
    }
}
