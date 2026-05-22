namespace Agnosia.Models;

public enum AppPermissionRiskConfidence
{
    None,
    Medium,
    High
}

public sealed record AppPermissionRiskScoreBreakdown(
    int DataSensitivityScore,
    int PersistenceScore,
    int ExfiltrationScore,
    int ControlSurfaceScore,
    int StealthScore,
    int LegitimacyPenalty,
    int ConfidenceScore)
{
    public int Total =>
        Math.Max(
            0,
            DataSensitivityScore
            + PersistenceScore
            + ExfiltrationScore
            + ControlSurfaceScore
            + StealthScore
            + ConfidenceScore
            - LegitimacyPenalty);

    public static AppPermissionRiskScoreBreakdown Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

public sealed record AppPermissionRiskAnalysis
{
    public AppPermissionRiskAnalysis(
        AppPermissionRiskLevel level,
        IReadOnlyList<string> riskyPermissions)
        : this(level, riskyPermissions, [], 0, 0, AppPermissionRiskConfidence.None, AppPermissionRiskScoreBreakdown.Empty, [], [])
    {
    }

    public AppPermissionRiskAnalysis(
        AppPermissionRiskLevel level,
        IReadOnlyList<string> riskyPermissions,
        IReadOnlyList<string> matchedRuleIds,
        int score,
        int rawScore,
        AppPermissionRiskConfidence confidence)
        : this(level, riskyPermissions, matchedRuleIds, score, rawScore, confidence, AppPermissionRiskScoreBreakdown.Empty, [], [])
    {
    }

    public AppPermissionRiskAnalysis(
        AppPermissionRiskLevel level,
        IReadOnlyList<string> riskyPermissions,
        IReadOnlyList<string> matchedRuleIds,
        int score,
        int rawScore,
        AppPermissionRiskConfidence confidence,
        AppPermissionRiskScoreBreakdown scoreBreakdown)
        : this(level, riskyPermissions, matchedRuleIds, score, rawScore, confidence, scoreBreakdown, [], [])
    {
    }

    public AppPermissionRiskAnalysis(
        AppPermissionRiskLevel level,
        IReadOnlyList<string> riskyPermissions,
        IReadOnlyList<string> matchedRuleIds,
        int score,
        int rawScore,
        AppPermissionRiskConfidence confidence,
        AppPermissionRiskScoreBreakdown scoreBreakdown,
        IReadOnlyList<string> manifestPermissions,
        IReadOnlyList<string> runtimePermissions)
    {
        Level = level;
        RiskyPermissions = riskyPermissions;
        MatchedRuleIds = matchedRuleIds;
        Score = score;
        RawScore = rawScore;
        Confidence = confidence;
        ScoreBreakdown = scoreBreakdown;
        ManifestPermissions = manifestPermissions;
        RuntimePermissions = runtimePermissions;
    }

    public AppPermissionRiskLevel Level { get; init; }

    public IReadOnlyList<string> RiskyPermissions { get; init; }

    public IReadOnlyList<string> MatchedRuleIds { get; init; }

    public int Score { get; init; }

    public int RawScore { get; init; }

    public AppPermissionRiskConfidence Confidence { get; init; }

    public AppPermissionRiskScoreBreakdown ScoreBreakdown { get; init; }

    public IReadOnlyList<string> ManifestPermissions { get; init; }

    public IReadOnlyList<string> RuntimePermissions { get; init; }

    public static AppPermissionRiskAnalysis Safe { get; } = new(AppPermissionRiskLevel.Safe, []);
}
