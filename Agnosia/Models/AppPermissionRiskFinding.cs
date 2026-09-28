namespace Agnosia.Models;

public enum AppPermissionRiskEvidenceState
{
    Declared,
    Granted,
    Denied,
    Enabled,
    Observed,
    Unknown,
    ForegroundOnly,
    Disabled
}

public sealed record AppPermissionRiskEvidence(string SignalId, AppPermissionRiskEvidenceState State);

public sealed record AppPermissionRiskFinding(
    string RuleId,
    AppPermissionRiskLevel RuleLevel,
    IReadOnlyList<AppPermissionRiskEvidence> Evidence);
