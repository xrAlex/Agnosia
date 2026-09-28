using Agnosia.Models;

namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private static int CalculateRawScore(
        AnalysisContext context,
        IReadOnlyList<MatchedRule> matchedRules)
    {
        if (matchedRules.Count == 0) return 0;

        return AddAppLevelScoreBreakdown(context, SumBreakdowns(matchedRules.Select(match => match.ScoreBreakdown))).Total;
    }

    private static AppPermissionRiskScoreBreakdown CalculateGroupedScoreBreakdown(
        AnalysisContext context,
        IReadOnlyList<MatchedRule> matchedRules)
    {
        var breakdownByGroup = new Dictionary<string, AppPermissionRiskScoreBreakdown>(StringComparer.Ordinal);
        foreach (var match in matchedRules)
        {
            var breakdown = match.ScoreBreakdown;
            if (breakdownByGroup.TryGetValue(match.Rule.GroupId, out var current)
                && breakdown.Total <= current.Total) continue;

            breakdownByGroup[match.Rule.GroupId] = breakdown;
        }

        return matchedRules.Count == 0
            ? AppPermissionRiskScoreBreakdown.Empty
            : AddAppLevelScoreBreakdown(context, SumBreakdowns(breakdownByGroup.Values));
    }

    private static AppPermissionRiskScoreBreakdown AddAppLevelScoreBreakdown(
        AnalysisContext context,
        AppPermissionRiskScoreBreakdown breakdown)
    {
        return breakdown with
        {
            ExfiltrationScore = breakdown.ExfiltrationScore + context.GetExfiltrationScore()
        };
    }

    private static AppPermissionRiskScoreBreakdown SumBreakdowns(
        IEnumerable<AppPermissionRiskScoreBreakdown> breakdowns)
    {
        var dataSensitivityScore = 0;
        var persistenceScore = 0;
        var exfiltrationScore = 0;
        var controlSurfaceScore = 0;
        var stealthScore = 0;
        var legitimacyPenalty = 0;

        foreach (var breakdown in breakdowns)
        {
            dataSensitivityScore += breakdown.DataSensitivityScore;
            persistenceScore = Math.Max(persistenceScore, breakdown.PersistenceScore);
            exfiltrationScore += breakdown.ExfiltrationScore;
            controlSurfaceScore += breakdown.ControlSurfaceScore;
            stealthScore = Math.Max(stealthScore, breakdown.StealthScore);
            legitimacyPenalty += breakdown.LegitimacyPenalty;
        }

        return new AppPermissionRiskScoreBreakdown(
            dataSensitivityScore,
            persistenceScore,
            exfiltrationScore,
            controlSurfaceScore,
            stealthScore,
            legitimacyPenalty,
            0);
    }
}
