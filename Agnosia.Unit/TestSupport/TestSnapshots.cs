using Agnosia.Models;

namespace Agnosia.Unit.TestSupport;

internal static class TestSnapshots
{
    public static DashboardSnapshot Dashboard(
        bool hasSetup = true,
        bool workProfileAvailable = true,
        WorkProfileStateKind workProfileState = WorkProfileStateKind.Available,
        WorkProfileRecoveryKind workProfileRecovery = WorkProfileRecoveryKind.None,
        string workProfileDiagnosticReason = "",
        AppSettingsSnapshot? settings = null,
        IReadOnlyList<AppSnapshot>? personalApps = null,
        IReadOnlyList<AppSnapshot>? workApps = null)
    {
        return new DashboardSnapshot(
            true,
            hasSetup,
            false,
            workProfileAvailable,
            workProfileState,
            workProfileRecovery,
            workProfileDiagnosticReason,
            personalApps ?? [],
            workApps ?? [],
            settings ?? AppSettingsSnapshot.Default);
    }

    public static AppSnapshot App(
        ProfileKind profile,
        string packageName = "com.example.app",
        string label = "Example",
        bool isSystem = false,
        bool isHidden = false,
        bool canLaunch = true,
        bool isInstalled = true,
        bool interactionAllowed = false,
        AppPermissionRiskLevel permissionRiskLevel = AppPermissionRiskLevel.Safe,
        IReadOnlyList<string>? riskyPermissions = null,
        IReadOnlyList<string>? matchedPermissionRiskRuleIds = null,
        AppPermissionRiskScoreBreakdown? permissionRiskScoreBreakdown = null,
        IReadOnlyList<string>? manifestPermissions = null,
        IReadOnlyList<string>? runtimePermissions = null)
    {
        return new AppSnapshot(
            packageName,
            label,
            null,
            [],
            profile,
            isSystem,
            isHidden,
            canLaunch,
            isInstalled,
            interactionAllowed,
            PermissionRiskLevel: permissionRiskLevel,
            RiskyPermissions: riskyPermissions,
            MatchedPermissionRiskRuleIds: matchedPermissionRiskRuleIds,
            PermissionRiskScoreBreakdown: permissionRiskScoreBreakdown,
            ManifestPermissions: manifestPermissions,
            RuntimePermissions: runtimePermissions);
    }

    public static PermissionSnapshot GrantedPermission(PermissionKind kind)
    {
        return Permission(kind, true);
    }

    public static PermissionSnapshot RequiredPermission(PermissionKind kind)
    {
        return Permission(kind, false);
    }

    public static IReadOnlyList<PermissionSnapshot> RequiredOnboardingPermissions(bool granted)
    {
        PermissionKind[] requiredKinds =
        [
            PermissionKind.WorkProfile,
            PermissionKind.UsageStats,
            PermissionKind.Notifications,
            PermissionKind.VpnControl,
            PermissionKind.PackageInstall,
            PermissionKind.Overlay
        ];

        return requiredKinds
            .Select(kind => Permission(kind, granted))
            .ToArray();
    }

    public static PermissionSnapshot Permission(
        PermissionKind kind,
        bool isGranted,
        bool canRequest = true,
        string grantedLabel = "Granted",
        string requestLabel = "Request")
    {
        return new PermissionSnapshot(
            kind,
            $"{kind} title",
            "Profile",
            $"{kind} description",
            isGranted,
            canRequest,
            grantedLabel,
            requestLabel);
    }

    public static AgnosiaModuleSnapshot FileShuttleModule(
        bool isEnabled = false,
        AgnosiaModuleState state = AgnosiaModuleState.Disabled,
        IReadOnlyList<AgnosiaModuleRequirement>? requirements = null,
        bool canSetEnabled = true)
    {
        return new AgnosiaModuleSnapshot(
            AgnosiaModuleKind.FileShuttle,
            "File Shuttle",
            "Short file shuttle description",
            "Full file shuttle description",
            isEnabled,
            state,
            requirements ?? [],
            state switch
            {
                AgnosiaModuleState.Enabled => "Включён",
                AgnosiaModuleState.PartiallyEnabled => "Требует разрешений",
                AgnosiaModuleState.Unavailable => "Недоступен",
                _ => "Выключен"
            },
            canSetEnabled);
    }

    public static AgnosiaModuleRequirement ModuleRequirement(
        PermissionKind? permissionKind,
        bool isSatisfied,
        string title = "Requirement")
    {
        return new AgnosiaModuleRequirement(
            title,
            $"{title} description",
            isSatisfied,
            permissionKind,
            "Request");
    }
}
