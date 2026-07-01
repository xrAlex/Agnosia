using System.Text.Json;
using Agnosia.Android.Commands.Handlers;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private const int DefaultQueryAppsPageLimit = 100;
    private const int DefaultQueryAppsMaxJsonBytes = 512 * 1024;

    private static Intent CreateQueryAppsResult(
        Intent request,
        QueryAppsResponse response)
    {
        var interactionPackages = response.InteractionPackages ?? [];
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultAppsJson, response.AppsJson);
        if (!IsPagedQuery(request))
        {
            result.PutExtra(AndroidCommandContract.ResultInteractionPackages, interactionPackages);
            return result;
        }

        result.PutExtra(AndroidCommandContract.ResultNextQueryOffset, response.NextOffset);
        result.PutExtra(AndroidCommandContract.ResultQueryHasMore, response.HasMore);
        result.PutExtra(AndroidCommandContract.ResultQueryTotalCount, response.TotalCount);

        if (interactionPackages.Length > 0)
            result.PutExtra(AndroidCommandContract.ResultInteractionPackages, interactionPackages);

        Log.Debug(
            LogTag,
            $"Query apps page prepared. nextOffset={response.NextOffset}, hasMore={response.HasMore}, totalCount={response.TotalCount}.");
        return result;
    }

    private static bool IsPagedQuery(Intent request)
    {
        return request.HasExtra(AndroidCommandContract.ExtraQueryPageToken)
               || request.HasExtra(AndroidCommandContract.ExtraQueryOffset)
               || request.HasExtra(AndroidCommandContract.ExtraQueryLimit)
               || request.HasExtra(AndroidCommandContract.ExtraQueryMaxJsonBytes);
    }

    private static string? CreateCommandRequestPayloadJson(AndroidCommandKind kind, Intent? intent)
    {
        var existingPayload = intent?.GetStringExtra(AndroidCommandIntentMapper.PayloadJsonExtraKey);
        if (!string.IsNullOrWhiteSpace(existingPayload)) return existingPayload;

        return kind switch
        {
            AndroidCommandKind.QueryApps => JsonSerializer.Serialize(new QueryAppsRequest(
                intent?.GetBooleanExtra(AndroidCommandContract.ExtraShowAll, false) == true,
                intent?.GetStringExtra(AndroidCommandContract.ExtraQueryPageToken),
                intent?.GetIntExtra(AndroidCommandContract.ExtraQueryOffset, 0) ?? 0,
                intent?.GetIntExtra(AndroidCommandContract.ExtraQueryLimit, DefaultQueryAppsPageLimit) ??
                DefaultQueryAppsPageLimit,
                intent?.GetIntExtra(AndroidCommandContract.ExtraQueryMaxJsonBytes, DefaultQueryAppsMaxJsonBytes) ??
                DefaultQueryAppsMaxJsonBytes)),
            AndroidCommandKind.QueryAppIcon => JsonSerializer.Serialize(new QueryAppIconRequest(
                intent?.GetStringExtra(AndroidCommandContract.ExtraPackage))),
            AndroidCommandKind.QueryAppIcons => JsonSerializer.Serialize(new QueryAppIconsRequest(
                intent?.GetStringArrayExtra(AndroidCommandContract.ExtraPackages) ?? [])),
            _ => null
        };
    }

    private static Intent CreateCommandResultIntent(
        AndroidCommandKind kind,
        string? payloadJson,
        string diagnostics,
        Intent? request)
    {
        var result = new Intent();
        if (!string.IsNullOrWhiteSpace(payloadJson))
            result.PutExtra(AndroidCommandIntentMapper.PayloadJsonExtraKey, payloadJson);
        if (!string.IsNullOrWhiteSpace(diagnostics))
            result.PutExtra(AndroidCommandIntentMapper.DiagnosticsExtraKey, diagnostics);

        switch (kind)
        {
            case AndroidCommandKind.ProfilePing:
                PutBooleanPayload(result, payloadJson, AndroidCommandContract.ResultProfileOwnerCheckPerformed);
                PutBooleanPayload(result, payloadJson, AndroidCommandContract.ResultIsProfileOwner);
                PutLongPayload(result, payloadJson, AndroidCommandContract.ResultAppVersionCode);
                PutStringPayload(result, payloadJson, AndroidCommandContract.ResultAppVersionName);
                break;
            case AndroidCommandKind.QueryApps:
                if (DeserializePayload<QueryAppsResponse>(payloadJson) is { } appsResponse)
                {
                    var appsResult = CreateQueryAppsResult(request ?? result, appsResponse);
                    if (!string.IsNullOrWhiteSpace(payloadJson))
                        appsResult.PutExtra(AndroidCommandIntentMapper.PayloadJsonExtraKey, payloadJson);
                    if (!string.IsNullOrWhiteSpace(diagnostics))
                        appsResult.PutExtra(AndroidCommandIntentMapper.DiagnosticsExtraKey, diagnostics);
                    return appsResult;
                }

                break;
            case AndroidCommandKind.QueryAppIcon:
                if (DeserializePayload<QueryAppIconResponse>(payloadJson) is { IconPng: { Length: > 0 } iconPng })
                    result.PutExtra(AndroidCommandContract.ResultIconPng, iconPng);
                break;
            case AndroidCommandKind.QueryAppIcons:
                if (DeserializePayload<QueryAppIconsResponse>(payloadJson) is { Icons: { } icons })
                    result.PutExtra(AndroidCommandContract.ResultIconsBundle, CreateIconsBundle(icons));
                break;
            case AndroidCommandKind.QueryLogs:
                PutStringPayload(result, payloadJson, AndroidCommandContract.ResultLogsJson);
                break;
            case AndroidCommandKind.QueryCrossProfilePackages:
                result.PutExtra(
                    AndroidCommandContract.ResultInteractionPackages,
                    ReadStringArrayPayload(payloadJson, AndroidCommandContract.ResultInteractionPackages));
                break;
            case AndroidCommandKind.QueryPermissions:
                PutBooleanPayload(result, payloadJson, AndroidCommandContract.ResultUsageStatsAccess);
                PutBooleanPayload(result, payloadJson, AndroidCommandContract.ResultPackageInstallAccess);
                PutBooleanPayload(result, payloadJson, AndroidCommandContract.ResultAllFilesAccess);
                break;
        }

        return result;
    }

    private static Bundle CreateIconsBundle(IReadOnlyDictionary<string, byte[]?> icons)
    {
        var bundle = new Bundle();
        foreach (var (packageName, iconPng) in icons)
            if (iconPng is { Length: > 0 })
                bundle.PutByteArray(packageName, iconPng);

        return bundle;
    }

    private static void PutStringPayload(Intent result, string? payloadJson, string propertyName)
    {
        var value = ReadStringPayload(payloadJson, propertyName);
        if (!string.IsNullOrWhiteSpace(value))
            result.PutExtra(propertyName, value);
    }

    private static void PutLongPayload(Intent result, string? payloadJson, string propertyName)
    {
        if (TryReadLongPayload(payloadJson, propertyName, out var value))
            result.PutExtra(propertyName, value);
    }

    private static void PutBooleanPayload(Intent result, string? payloadJson, string propertyName)
    {
        if (TryReadBooleanPayload(payloadJson, propertyName, out var value))
            result.PutExtra(propertyName, value);
    }

    private static string? ReadStringPayload(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                   && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to parse command payload '{propertyName}': {exception.Message}");
            return null;
        }
    }

    private static bool TryReadBooleanPayload(string? payloadJson, string propertyName, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                return false;

            value = property.GetBoolean();
            return true;
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to parse command payload '{propertyName}': {exception.Message}");
            return false;
        }
    }

    private static bool TryReadLongPayload(string? payloadJson, string propertyName, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                   && property.TryGetInt64(out value);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to parse command payload '{propertyName}': {exception.Message}");
            return false;
        }
    }

    private static string[] ReadStringArrayPayload(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return [];

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return ReadStringArray(root);

            return root.TryGetProperty(propertyName, out var property)
                ? ReadStringArray(property)
                : [];
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to parse command payload '{propertyName}': {exception.Message}");
            return [];
        }
    }

    private static string[] ReadStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return [];

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static void ClearAppInventoryQueryCache()
    {
        AndroidQueryCache.Shared.ClearAppInventoryQueries();
    }

    private static T? DeserializePayload<T>(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(payloadJson);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to deserialize command payload as {typeof(T).Name}: {exception.Message}");
            return default;
        }
    }

    private void ActionRequestUsageStatsAccess()
    {
        if (AndroidUsageStatsAccessApi.HasAccess(this, LogTag))
        {
            FinishWithSuccessMessage("Доступ к истории использования уже включен.");
            return;
        }

        if (AndroidUsageStatsAccessApi.TryOpenSettings(this, LogTag, FinishWithError))
            FinishWithSuccessMessage(
                "Откройте Agnosia в настройках Android и включите доступ к истории использования.");
    }

    private void ActionRequestPackageInstallAccess()
    {
        if (AndroidPackageApi.CanRequestInstalls(this, LogTag))
        {
            FinishWithSuccessMessage("Доступ к установке APK уже включен.");
            return;
        }

        if (AndroidPackageApi.TryOpenUnknownSourcesSettings(this, LogTag, FinishWithError))
            FinishWithSuccessMessage("Включите установку APK из Agnosia в рабочем профиле.");
    }

    private void ActionRequestAllFilesAccess()
    {
        if (AndroidPermissionApi.HasAllFilesAccess(this))
        {
            FinishWithSuccessMessage("Доступ ко всем файлам уже включен.");
            return;
        }

        var result = AndroidPermissionApi.OpenAllFilesAccessSettings(this);
        if (result.Succeeded)
            FinishWithSuccessMessage(result.Message);
        else
            FinishWithError(result.Message);
    }
}
