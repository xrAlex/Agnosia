using Agnosia.Models;

namespace Agnosia.Unit.TestSupport;

internal static class TestSnapshots
{
    public static DashboardSnapshot Dashboard(
        bool hasSetup = true,
        bool workProfileAvailable = true,
        WorkProfileStateKind workProfileState = WorkProfileStateKind.AppIsProfileOwner,
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
        bool interactionAllowed = true)
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
            interactionAllowed);
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
}
