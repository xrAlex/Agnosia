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
    IReadOnlyList<string>? RuntimePermissions = null);
