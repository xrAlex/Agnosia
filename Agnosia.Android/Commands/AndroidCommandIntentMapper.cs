using Agnosia.Android.Api.Commands;
using System.Text.Json;

#if AGNOSIA_ANDROID
using Android.Content;
using Android.OS;
#endif

namespace Agnosia.Android.Commands;

internal static class AndroidCommandIntentMapper
{
    public const string PayloadJsonExtraKey = "agnosia.command.payload_json";
    public const string DiagnosticsExtraKey = "agnosia.command.diagnostics";

    public static string ToAction(AndroidCommandKind kind)
    {
        return kind switch
        {
            AndroidCommandKind.ProfilePing => AgnosiaActions.ProfilePing,
            AndroidCommandKind.QueryApps => AgnosiaActions.QueryApps,
            AndroidCommandKind.QueryAppIcon => AgnosiaActions.QueryAppIcon,
            AndroidCommandKind.QueryAppIcons => AgnosiaActions.QueryAppIcons,
            AndroidCommandKind.QueryLogs => AgnosiaActions.QueryLogs,
            AndroidCommandKind.QueryCrossProfilePackages => AgnosiaActions.QueryCrossProfilePackages,
            AndroidCommandKind.QueryPermissions => AgnosiaActions.QueryPermissions,
            AndroidCommandKind.QueryUsageStatsAccess => AgnosiaActions.QueryUsageStatsAccess,
            AndroidCommandKind.QueryPackageInstallAccess => AgnosiaActions.QueryPackageInstallAccess,
            AndroidCommandKind.QueryAllFilesAccess => AgnosiaActions.QueryAllFilesAccess,
            AndroidCommandKind.RequestUsageStatsAccess => AgnosiaActions.RequestUsageStatsAccess,
            AndroidCommandKind.RequestPackageInstallAccess => AgnosiaActions.RequestPackageInstallAccess,
            AndroidCommandKind.RequestAllFilesAccess => AgnosiaActions.RequestAllFilesAccess,
            AndroidCommandKind.InstallPackage => AgnosiaActions.InstallPackage,
            AndroidCommandKind.UninstallPackage => AgnosiaActions.UninstallPackage,
            AndroidCommandKind.FreezePackage => AgnosiaActions.FreezePackage,
            AndroidCommandKind.UnfreezePackage => AgnosiaActions.UnfreezePackage,
            AndroidCommandKind.RevokeRuntimePermissions => AgnosiaActions.RevokeRuntimePermissions,
            AndroidCommandKind.SetLockdownEnabled => AgnosiaActions.SetLockdownEnabled,
            AndroidCommandKind.SetLockdownInternetAccess => AgnosiaActions.SetLockdownInternetAccess,
            AndroidCommandKind.PrepareHiddenShortcut => AgnosiaActions.PrepareHiddenShortcut,
            AndroidCommandKind.CreateHiddenShortcut => AgnosiaActions.CreateHiddenShortcut,
            AndroidCommandKind.UnfreezeAndLaunch => AgnosiaActions.UnfreezeAndLaunch,
            AndroidCommandKind.LaunchAppProxy => AgnosiaActions.LaunchAppProxy,
            AndroidCommandKind.SetCrossProfileInteraction => AgnosiaActions.SetCrossProfileInteraction,
            AndroidCommandKind.StartFileShuttleParentToWork => AgnosiaActions.StartFileShuttleParentToWork,
            AndroidCommandKind.StartFileShuttleWorkToParent => AgnosiaActions.StartFileShuttleWorkToParent,
            AndroidCommandKind.SynchronizePreference => AgnosiaActions.SynchronizePreference,
            AndroidCommandKind.WorkAppFrozen => AgnosiaActions.WorkAppFrozen,
            AndroidCommandKind.FinalizeProvision => AgnosiaActions.FinalizeProvision,
            AndroidCommandKind.RecoverAuthentication => AgnosiaActions.RecoverAuthentication,
            AndroidCommandKind.PackageInstallerCallback => AgnosiaActions.PackageInstallerCallback,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No Android action is mapped for command kind.")
        };
    }

#if AGNOSIA_ANDROID
    public static Intent ToIntent(AndroidCommandEnvelope envelope)
    {
        var intent = new Intent(ToAction(envelope.Kind));
        if (envelope.PayloadJson is not null)
        {
            intent.PutExtra(PayloadJsonExtraKey, envelope.PayloadJson);
            ApplyPayloadJsonToLegacyExtras(envelope.Kind, envelope.PayloadJson, intent);
        }

        return intent;
    }

    public static string? ReadPayloadJson(AndroidCommandEnvelope envelope, Intent? intent)
    {
        if (intent is null) return null;

        var payloadJson = intent.GetStringExtra(PayloadJsonExtraKey);
        if (!string.IsNullOrWhiteSpace(payloadJson)) return payloadJson;

        return envelope.Kind switch
        {
            AndroidCommandKind.ProfilePing => SerializeResultPayload(intent, new Dictionary<string, object?>
            {
                [AndroidCommandContract.ResultProfileOwnerCheckPerformed] = intent.GetBooleanExtra(AndroidCommandContract.ResultProfileOwnerCheckPerformed, false),
                [AndroidCommandContract.ResultIsProfileOwner] = intent.GetBooleanExtra(AndroidCommandContract.ResultIsProfileOwner, false),
                [AndroidCommandContract.ResultAppVersionCode] = intent.GetLongExtra(AndroidCommandContract.ResultAppVersionCode, 0),
                [AndroidCommandContract.ResultAppVersionName] = intent.GetStringExtra(AndroidCommandContract.ResultAppVersionName)
            }),
            AndroidCommandKind.QueryApps => SerializeResultPayload(intent, new Dictionary<string, object?>
            {
                [AndroidCommandContract.ResultAppsJson] = intent.GetStringExtra(AndroidCommandContract.ResultAppsJson),
                [AndroidCommandContract.ResultInteractionPackages] = intent.GetStringArrayExtra(AndroidCommandContract.ResultInteractionPackages) ?? [],
                [AndroidCommandContract.ResultNextQueryOffset] = intent.GetIntExtra(AndroidCommandContract.ResultNextQueryOffset, 0),
                [AndroidCommandContract.ResultQueryHasMore] = intent.GetBooleanExtra(AndroidCommandContract.ResultQueryHasMore, false),
                [AndroidCommandContract.ResultQueryTotalCount] = intent.GetIntExtra(AndroidCommandContract.ResultQueryTotalCount, 0)
            }),
            AndroidCommandKind.QueryAppIcon => SerializeResultPayload(intent, new Dictionary<string, object?>
            {
                [AndroidCommandContract.ResultIconPng] = ToBase64(intent.GetByteArrayExtra(AndroidCommandContract.ResultIconPng))
            }),
            AndroidCommandKind.QueryAppIcons => SerializeResultPayload(intent, new Dictionary<string, object?>
            {
                [AndroidCommandContract.ResultIconsBundle] = ReadIconBundle(intent.GetBundleExtra(AndroidCommandContract.ResultIconsBundle))
            }),
            AndroidCommandKind.QueryLogs => SerializeResultPayload(intent, new Dictionary<string, object?>
            {
                [AndroidCommandContract.ResultLogsJson] = intent.GetStringExtra(AndroidCommandContract.ResultLogsJson)
            }),
            AndroidCommandKind.QueryCrossProfilePackages => SerializeResultPayload(intent, new Dictionary<string, object?>
            {
                [AndroidCommandContract.ResultInteractionPackages] = intent.GetStringArrayExtra(AndroidCommandContract.ResultInteractionPackages) ?? []
            }),
            AndroidCommandKind.QueryPermissions => SerializePermissionPayload(intent),
            AndroidCommandKind.QueryUsageStatsAccess => SerializeBooleanPayload(intent, AndroidCommandContract.ResultUsageStatsAccess),
            AndroidCommandKind.QueryPackageInstallAccess => SerializeBooleanPayload(intent, AndroidCommandContract.ResultPackageInstallAccess),
            AndroidCommandKind.QueryAllFilesAccess => SerializeBooleanPayload(intent, AndroidCommandContract.ResultAllFilesAccess),
            _ => SerializeResultPayload(intent, [])
        };
    }

    public static string ReadDiagnostics(Intent? intent)
    {
        return intent?.GetStringExtra(DiagnosticsExtraKey) ?? string.Empty;
    }

    private static void ApplyPayloadJsonToLegacyExtras(
        AndroidCommandKind kind,
        string payloadJson,
        Intent intent)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        PutString(root, intent, AndroidCommandContract.ExtraPackage, "PackageName", "Package");
        PutStringArray(root, intent, AndroidCommandContract.ExtraPackages, "PackageNames", "Packages");
        PutStringArray(root, intent, AndroidCommandContract.ExtraPermissions, "Permissions");
        PutBoolean(root, intent, AndroidCommandContract.ExtraIsSystem, "IsSystem");
        PutString(root, intent, AndroidCommandContract.ExtraApk, "Apk", "SourceDirectory");
        PutStringArray(root, intent, AndroidCommandContract.ExtraSplitApks, "SplitApks");
        PutBoolean(root, intent, AndroidCommandContract.ExtraShowAll, "ShowAll");
        PutString(root, intent, AndroidCommandContract.ExtraPreferenceName, "Name", "PreferenceName");
        PutBoolean(root, intent, AndroidCommandContract.ExtraPreferenceBoolean, "Boolean", "Enabled");
        PutBoolean(root, intent, AndroidCommandContract.ExtraInternetBlocked, "InternetBlocked", "Blocked");
        PutString(root, intent, AndroidCommandContract.ExtraLaunchPackageName, "PackageName", "LaunchPackageName");
        PutString(root, intent, AndroidCommandContract.ExtraLaunchDisplayName, "DisplayName", "LaunchDisplayName");
        PutString(root, intent, AndroidCommandContract.ExtraShortcutTargetActivity, "TargetActivity");
        PutString(root, intent, AndroidCommandContract.ExtraShortcutLabel, "Label", "ShortcutLabel");
        PutString(root, intent, AndroidCommandContract.ExtraShortcutIconBase64, "IconBase64", "ShortcutIconBase64");
        PutString(root, intent, AndroidCommandContract.ExtraShortcutToken, "ShortcutToken");
        PutString(root, intent, AndroidCommandContract.ExtraTrigger, "Trigger");
        PutString(root, intent, AndroidCommandContract.ExtraReplacementAuthKey, "ReplacementAuthKey");
        PutInt(root, intent, AndroidCommandContract.ExtraQueryOffset, "Offset");
        PutInt(root, intent, AndroidCommandContract.ExtraQueryLimit, "Limit");
        PutInt(root, intent, AndroidCommandContract.ExtraQueryMaxJsonBytes, "MaxJsonBytes");
        PutString(root, intent, AndroidCommandContract.ExtraQueryPageToken, "PageToken");

        if (kind == AndroidCommandKind.QueryAppIcons)
            PutStringArray(root, intent, AndroidCommandContract.ExtraPackages, "PackageNames");
    }

    private static string SerializePermissionPayload(Intent intent)
    {
        return SerializeResultPayload(intent, new Dictionary<string, object?>
        {
            [AndroidCommandContract.ResultUsageStatsAccess] = intent.GetBooleanExtra(AndroidCommandContract.ResultUsageStatsAccess, false),
            [AndroidCommandContract.ResultPackageInstallAccess] = intent.GetBooleanExtra(AndroidCommandContract.ResultPackageInstallAccess, false),
            [AndroidCommandContract.ResultAllFilesAccess] = intent.GetBooleanExtra(AndroidCommandContract.ResultAllFilesAccess, false)
        });
    }

    private static string SerializeBooleanPayload(Intent intent, string resultName)
    {
        return SerializeResultPayload(intent, new Dictionary<string, object?>
        {
            [resultName] = intent.GetBooleanExtra(resultName, false)
        });
    }

    private static string SerializeResultPayload(Intent intent, Dictionary<string, object?> values)
    {
        AddStringIfPresent(values, intent, AndroidCommandContract.ResultMessage);
        AddStringIfPresent(values, intent, AndroidCommandContract.ResultError);
        AddBooleanIfPresent(values, intent, AndroidCommandContract.ResultHideImmediately);
        AddBooleanIfPresent(values, intent, AndroidCommandContract.ResultPreHideSucceeded);
        AddStringIfPresent(values, intent, AndroidCommandContract.ResultLaunchJson);
        AddBooleanIfPresent(values, intent, AndroidCommandContract.ResultToggleSuccess);

        return SerializeObject(values);
    }

    private static void AddStringIfPresent(Dictionary<string, object?> values, Intent intent, string extraName)
    {
        if (intent.HasExtra(extraName)) values[extraName] = intent.GetStringExtra(extraName);
    }

    private static void AddBooleanIfPresent(Dictionary<string, object?> values, Intent intent, string extraName)
    {
        if (intent.HasExtra(extraName)) values[extraName] = intent.GetBooleanExtra(extraName, false);
    }

    private static IReadOnlyDictionary<string, byte[]?>? ReadIconBundle(Bundle? bundle)
    {
        if (bundle is null) return null;

        var icons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var key in bundle.KeySet() ?? [])
            icons[key] = bundle.GetByteArray(key);

        return icons;
    }

    private static string? ToBase64(byte[]? bytes)
    {
        return bytes is { Length: > 0 } ? Convert.ToBase64String(bytes) : null;
    }

    private static void PutString(JsonElement root, Intent intent, string extraName, params string[] aliases)
    {
        if (TryGetProperty(root, extraName, aliases, out var property) && property.ValueKind == JsonValueKind.String)
            intent.PutExtra(extraName, property.GetString());
    }

    private static void PutStringArray(JsonElement root, Intent intent, string extraName, params string[] aliases)
    {
        if (!TryGetProperty(root, extraName, aliases, out var property) || property.ValueKind != JsonValueKind.Array)
            return;

        var values = property.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        intent.PutExtra(extraName, values);
    }

    private static void PutBoolean(JsonElement root, Intent intent, string extraName, params string[] aliases)
    {
        if (TryGetProperty(root, extraName, aliases, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            intent.PutExtra(extraName, property.GetBoolean());
    }

    private static void PutInt(JsonElement root, Intent intent, string extraName, params string[] aliases)
    {
        if (TryGetProperty(root, extraName, aliases, out var property) && property.TryGetInt32(out var value))
            intent.PutExtra(extraName, value);
    }

    private static bool TryGetProperty(
        JsonElement root,
        string extraName,
        string[] aliases,
        out JsonElement property)
    {
        if (root.TryGetProperty(extraName, out property)) return true;

        foreach (var alias in aliases)
            if (root.TryGetProperty(alias, out property))
                return true;

        return false;
    }
#endif

    private static string SerializeObject<T>(T value)
    {
        return JsonSerializer.Serialize(value);
    }
}
