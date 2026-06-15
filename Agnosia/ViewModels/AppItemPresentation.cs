using Agnosia.Models;

namespace Agnosia.ViewModels;

internal static class AppItemPresentation
{
    public static bool ShouldShowPermissionRiskIndicator(AppSnapshot snapshot)
    {
        return snapshot.PermissionRiskAvailable
            && !snapshot.IsSystem
            && snapshot.PermissionRiskLevel != AppPermissionRiskLevel.Safe;
    }

    public static string GetPermissionRiskTooltip(AppPermissionRiskLevel riskLevel, string summaryText)
    {
        return riskLevel switch
        {
            AppPermissionRiskLevel.Critical => summaryText,
            AppPermissionRiskLevel.Dangerous => summaryText,
            _ => "Разрешения: OK"
        };
    }

    public static string GetMonogram(string label)
    {
        return string.IsNullOrWhiteSpace(label)
            ? "?"
            : char.ToUpperInvariant(label[0]).ToString();
    }

    public static string GetStatusTagLabel(AppSnapshot snapshot)
    {
        if (snapshot.Profile == ProfileKind.Work && snapshot.IsHidden) return "Isolated";

        if (!snapshot.IsInstalled) return "NotInstalled";

        if (snapshot.IsSystem) return "System";

        return string.Empty;
    }

    public static string GetProfileLabel(ProfileKind profile)
    {
        return profile == ProfileKind.Work ? "Work" : "Personal";
    }

    public static bool ShouldShowSecondaryRow(AppSnapshot snapshot)
    {
        return GetStatusTagLabel(snapshot).Length > 0 || snapshot.Profile == ProfileKind.Work;
    }

    public static bool ShouldShowWorkControls(ProfileKind profile)
    {
        return profile == ProfileKind.Work;
    }

    public static bool IsAgnosiaManaged(AppSnapshot snapshot)
    {
        return ShouldShowWorkControls(snapshot.Profile) && snapshot.IsHidden;
    }

    public static bool CanClone(AppSnapshot snapshot)
    {
        return snapshot.Profile == ProfileKind.Personal || !snapshot.IsSystem;
    }

    public static bool CanMoveToWork(AppSnapshot snapshot)
    {
        return snapshot.Profile == ProfileKind.Personal && CanClone(snapshot) && CanUninstall(snapshot);
    }

    public static bool CanUninstall(AppSnapshot snapshot)
    {
        return !snapshot.IsSystem;
    }

    public static bool CanFreeze(AppSnapshot snapshot)
    {
        return snapshot.Profile == ProfileKind.Work && !snapshot.IsSystem;
    }

    public static bool CanToggleInternetAccess(AppSnapshot snapshot)
    {
        return snapshot.Profile == ProfileKind.Work && !snapshot.IsSystem;
    }

    public static bool CanRevokeRuntimePermissions(AppSnapshot snapshot, bool hasRuntimePermissions)
    {
        return hasRuntimePermissions && !snapshot.IsSystem && snapshot.Profile == ProfileKind.Work;
    }

    public static bool ShouldShowLaunch(AppSnapshot snapshot)
    {
        return snapshot.CanLaunch || snapshot.Profile == ProfileKind.Work;
    }

    public static string GetLaunchLabel(AppSnapshot snapshot)
    {
        return snapshot.Profile == ProfileKind.Work && snapshot.IsHidden ? "UnfreezeAndOpen" : "Open";
    }

    public static string GetCloneLabel(ProfileKind profile)
    {
        return profile == ProfileKind.Work ? "CopyToPersonal" : "CopyToWork";
    }

    public static string GetInteractionLabel(bool interactionAllowed)
    {
        return interactionAllowed ? "DisallowInteraction" : "AllowInteraction";
    }

    public static string GetInternetAccessLabel(bool isInternetBlocked)
    {
        return isInternetBlocked ? "UnblockInternet" : "BlockInternet";
    }
}
