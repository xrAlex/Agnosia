using Agnosia.Models;

namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private const int BaseDangerousScoreThreshold = 4;
    private const int CriticalScoreThreshold = 8;

    public static AppPermissionRiskLevel Classify(IEnumerable<string>? requestedPermissions)
    {
        return Analyze(requestedPermissions).Level;
    }

    public static AppPermissionRiskLevel Classify(AppPermissionRiskInput? input)
    {
        return Analyze(input).Level;
    }

    public static AppPermissionRiskAnalysis Analyze(IEnumerable<string>? requestedPermissions)
    {
        return Analyze(new AppPermissionRiskInput(requestedPermissions));
    }

    public static AppPermissionRiskAnalysis Analyze(AppPermissionRiskInput? input)
    {
        if (input is null) return AppPermissionRiskAnalysis.Safe;

        var context = AnalysisContext.Create(input);
        if (!context.HasAnySignal) return AppPermissionRiskAnalysis.Safe;

        var matchedRules = new List<PermissionCombinationRule>(CriticalRules.Length + DangerousRules.Length);
        var hasCriticalMatch = false;
        foreach (var rule in CriticalRules)
        {
            if (!rule.IsCriticalMatch(context)) continue;

            matchedRules.Add(rule);
            hasCriticalMatch = true;
        }

        foreach (var rule in DangerousRules)
        {
            if (rule.IsMatch(context)) matchedRules.Add(rule);
        }

        var rawScore = CalculateRawScore(context, matchedRules);
        var scoreBreakdown = CalculateGroupedScoreBreakdown(context, matchedRules);
        var score = scoreBreakdown.Total;

        if (hasCriticalMatch)
            return CreateAnalysis(
                AppPermissionRiskLevel.Critical,
                context,
                matchedRules,
                score,
                rawScore,
                scoreBreakdown);

        if (matchedRules.Count == 0 || score < context.DangerousScoreThreshold)
            return CreateSafeAnalysis(context);

        if (score >= CriticalScoreThreshold && context.HasHighConfidenceSignals)
            return CreateAnalysis(
                AppPermissionRiskLevel.Critical,
                context,
                matchedRules,
                score,
                rawScore,
                scoreBreakdown);

        return CreateAnalysis(
            AppPermissionRiskLevel.Dangerous,
            context,
            matchedRules,
            score,
            rawScore,
            scoreBreakdown);
    }

    private static AppPermissionRiskAnalysis CreateAnalysis(
        AppPermissionRiskLevel level,
        AnalysisContext context,
        IReadOnlyList<PermissionCombinationRule> matchedRules,
        int score,
        int rawScore,
        AppPermissionRiskScoreBreakdown scoreBreakdown)
    {
        return new AppPermissionRiskAnalysis(
            level,
            context.GetRiskyPermissions(matchedRules),
            GetMatchedRuleIds(matchedRules),
            score,
            rawScore,
            context.GetConfidence(),
            scoreBreakdown,
            context.GetManifestPermissions(),
            context.GetRuntimePermissions());
    }

    private static string[] GetMatchedRuleIds(IReadOnlyList<PermissionCombinationRule> matchedRules)
    {
        var ruleIds = new string[matchedRules.Count];
        for (var index = 0; index < matchedRules.Count; index++)
        {
            ruleIds[index] = matchedRules[index].Id;
        }

        return ruleIds;
    }

    private static AppPermissionRiskAnalysis CreateSafeAnalysis(AnalysisContext context)
    {
        return new AppPermissionRiskAnalysis(
            AppPermissionRiskLevel.Safe,
            [],
            [],
            0,
            0,
            AppPermissionRiskConfidence.None,
            AppPermissionRiskScoreBreakdown.Empty,
            context.GetManifestPermissions(),
            context.GetRuntimePermissions());
    }
}
