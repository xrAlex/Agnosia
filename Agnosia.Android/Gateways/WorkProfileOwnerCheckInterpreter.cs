using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Commands;

namespace Agnosia.Android.Gateways;

internal static class WorkProfileOwnerCheckInterpreter
{
    public static WorkProfileOwnerCheckResult Interpret(AndroidCommandResultEnvelope result)
    {
        if (!result.Succeeded)
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.Unreachable,
                string.IsNullOrWhiteSpace(result.ErrorCode)
                    ? $"profilePing=commandFailed; transport={result.Transport}; diagnostics={result.Diagnostics}"
                    : $"profilePing={result.ErrorCode}; transport={result.Transport}; diagnostics={result.Diagnostics}");

        if (string.IsNullOrWhiteSpace(result.PayloadJson))
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.Unreachable,
                $"profilePing=payloadMissing; transport={result.Transport}");

        try
        {
            using var document = JsonDocument.Parse(result.PayloadJson);
            var root = document.RootElement;
            if (!root.TryGetProperty(AndroidCommandContract.ResultProfileOwnerCheckPerformed, out var performedProperty)
                || performedProperty.ValueKind is not JsonValueKind.True and not JsonValueKind.False
                || !performedProperty.GetBoolean())
                return new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.Unreachable,
                    $"profilePing=payloadIncomplete; transport={result.Transport}");

            var isProfileOwner = root.TryGetProperty(AndroidCommandContract.ResultIsProfileOwner, out var ownerProperty)
                                 && ownerProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
                                 && ownerProperty.GetBoolean();
            var appVersionCode = root.TryGetProperty(AndroidCommandContract.ResultAppVersionCode, out var versionCodeProperty)
                                 && versionCodeProperty.TryGetInt64(out var parsedVersionCode)
                ? parsedVersionCode
                : 0;
            var appVersionName = root.TryGetProperty(AndroidCommandContract.ResultAppVersionName, out var versionNameProperty)
                                 && versionNameProperty.ValueKind == JsonValueKind.String
                ? versionNameProperty.GetString()
                : null;

            return isProfileOwner
                ? new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppIsProfileOwner,
                    $"commandCenter=true; transport={result.Transport}; {result.Diagnostics}",
                    appVersionCode,
                    appVersionName)
                : new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppInstalledButNotOwner,
                    $"commandCenter=true; transport={result.Transport}; {result.Diagnostics}",
                    appVersionCode,
                    appVersionName);
        }
        catch (JsonException exception)
        {
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.Unreachable,
                $"profilePing=payloadInvalid:{exception.GetType().Name}; transport={result.Transport}");
        }
    }
}
