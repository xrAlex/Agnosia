namespace Agnosia.Models;

public sealed record AppSnapshot(
    string PackageName,
    string Label,
    string? SourceDirectory,
    IReadOnlyList<string> SplitApks,
    ProfileKind Profile,
    bool IsSystem,
    bool IsHidden,
    bool CanLaunch,
    bool IsInstalled,
    bool InteractionAllowed,
    byte[]? IconPng = null,
    AppPermissionRiskLevel PermissionRiskLevel = AppPermissionRiskLevel.Safe,
    IReadOnlyList<string>? RiskyPermissions = null,
    IReadOnlyList<string>? MatchedPermissionRiskRuleIds = null,
    int PermissionRiskScore = 0,
    int PermissionRiskRawScore = 0,
    AppPermissionRiskConfidence PermissionRiskConfidence = AppPermissionRiskConfidence.None,
    AppPermissionRiskScoreBreakdown? PermissionRiskScoreBreakdown = null,
    IReadOnlyList<string>? ManifestPermissions = null,
    IReadOnlyList<string>? RuntimePermissions = null,
    bool PermissionRiskAvailable = true,
    bool IsInternetBlocked = false,
    bool IsIsolationEnabled = false,
    IReadOnlyList<AppPermissionRiskFinding>? PermissionRiskFindings = null,
    DateTimeOffset? PermissionRiskEvaluatedAtUtc = null,
    IReadOnlyList<string>? PermissionRiskUnavailableChecks = null)
{
    public AppSnapshot WithPermissionRiskFrom(AppSnapshot source) => this with
    {
        PermissionRiskAvailable = source.PermissionRiskAvailable,
        PermissionRiskLevel = source.PermissionRiskLevel,
        RiskyPermissions = source.RiskyPermissions,
        MatchedPermissionRiskRuleIds = source.MatchedPermissionRiskRuleIds,
        PermissionRiskScore = source.PermissionRiskScore,
        PermissionRiskRawScore = source.PermissionRiskRawScore,
        PermissionRiskConfidence = source.PermissionRiskConfidence,
        PermissionRiskScoreBreakdown = source.PermissionRiskScoreBreakdown,
        PermissionRiskFindings = source.PermissionRiskFindings,
        PermissionRiskEvaluatedAtUtc = source.PermissionRiskEvaluatedAtUtc,
        PermissionRiskUnavailableChecks = source.PermissionRiskUnavailableChecks,
        ManifestPermissions = source.ManifestPermissions,
        RuntimePermissions = source.RuntimePermissions
    };
}
