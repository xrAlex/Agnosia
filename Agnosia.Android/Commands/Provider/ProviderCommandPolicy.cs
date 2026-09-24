namespace Agnosia.Android.Commands;

internal static class ProviderCommandPolicy
{
    public static bool Supports(AndroidCommandKind kind) => kind is AndroidCommandKind.ProfilePing
        or AndroidCommandKind.QueryApps or AndroidCommandKind.QueryAppIcon or AndroidCommandKind.QueryAppIcons
        or AndroidCommandKind.QueryPermissions or AndroidCommandKind.QueryUsageStatsAccess
        or AndroidCommandKind.QueryAllFilesAccess or AndroidCommandKind.QueryPackageInstallAccess
        or AndroidCommandKind.QueryLogs or AndroidCommandKind.QueryCrossProfilePackages
        or AndroidCommandKind.QueryPackageState or AndroidCommandKind.FreezePackage
        or AndroidCommandKind.UnfreezePackage or AndroidCommandKind.ClearLogs;

    public static bool IsMutation(AndroidCommandKind kind) => kind is AndroidCommandKind.FreezePackage
        or AndroidCommandKind.UnfreezePackage or AndroidCommandKind.ClearLogs;

    public static bool CanFallbackToActivity(AndroidCommandKind kind, string? errorCode)
    {
        if (!IsMutation(kind)) return true;

        // These failures are known to happen before the mutation handler runs.
        return errorCode is "provider_disabled" or "grant_missing" or "legacy_profile"
            or "profile_unavailable" or "provider_unavailable" or "authentication_key_missing"
            or "authentication_failed" or "incompatible_version" or "wrong_profile"
            or "provider_busy" or "unsupported_command";
    }
}
