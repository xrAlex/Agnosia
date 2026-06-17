using Agnosia.Models;

namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private static int CalculateRawScore(
        AnalysisContext context,
        IReadOnlyList<PermissionCombinationRule> matchedRules)
    {
        if (matchedRules.Count == 0) return 0;

        var breakdowns = new AppPermissionRiskScoreBreakdown[matchedRules.Count];
        for (var index = 0; index < matchedRules.Count; index++)
        {
            breakdowns[index] = matchedRules[index].GetScoreBreakdown(context);
        }

        return AddAppLevelScoreBreakdown(context, SumBreakdowns(breakdowns)).Total;
    }

    private static AppPermissionRiskScoreBreakdown CalculateGroupedScoreBreakdown(
        AnalysisContext context,
        IReadOnlyList<PermissionCombinationRule> matchedRules)
    {
        var scoreByGroup = new Dictionary<string, int>(StringComparer.Ordinal);
        var breakdownByGroup = new Dictionary<string, AppPermissionRiskScoreBreakdown>(StringComparer.Ordinal);
        foreach (var rule in matchedRules)
        {
            var breakdown = rule.GetScoreBreakdown(context);
            var score = breakdown.Total;
            if (scoreByGroup.TryGetValue(rule.GroupId, out var currentScore) && score <= currentScore) continue;
            
            scoreByGroup[rule.GroupId] = score;
            breakdownByGroup[rule.GroupId] = breakdown;
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
            ExfiltrationScore = breakdown.ExfiltrationScore + context.GetExfiltrationScore(),
            ConfidenceScore = breakdown.ConfidenceScore + context.GetConfidenceScore()
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
        var confidenceScore = 0;

        foreach (var breakdown in breakdowns)
        {
            dataSensitivityScore += breakdown.DataSensitivityScore;
            persistenceScore += breakdown.PersistenceScore;
            exfiltrationScore += breakdown.ExfiltrationScore;
            controlSurfaceScore += breakdown.ControlSurfaceScore;
            stealthScore += breakdown.StealthScore;
            legitimacyPenalty += breakdown.LegitimacyPenalty;
            confidenceScore += breakdown.ConfidenceScore;
        }

        return new AppPermissionRiskScoreBreakdown(
            dataSensitivityScore,
            persistenceScore,
            exfiltrationScore,
            controlSurfaceScore,
            stealthScore,
            legitimacyPenalty,
            confidenceScore);
    }
}
