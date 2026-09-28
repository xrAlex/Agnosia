using Agnosia.Models;

namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private const int BaseDangerousScoreThreshold = 4;

    private readonly record struct MatchedRule(
        PermissionCombinationRule Rule,
        AppPermissionRiskScoreBreakdown ScoreBreakdown);

    internal static IReadOnlyList<string> AllRuleIds =>
        CriticalRules.Concat(DangerousRules).Select(rule => rule.Id).ToArray();

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

        var matchedRules = new List<MatchedRule>(8);
        var hasCriticalMatch = false;
        foreach (var rule in CriticalRules)
        {
            if (!rule.IsCriticalMatch(context)) continue;

            matchedRules.Add(new(rule, rule.GetScoreBreakdown(context)));
            hasCriticalMatch = true;
        }

        foreach (var rule in DangerousRules)
        {
            if (rule.IsMatch(context)) matchedRules.Add(new(rule, rule.GetScoreBreakdown(context)));
        }

        if (matchedRules.Count == 0) return CreateSafeAnalysis(context);

        var rawScore = CalculateRawScore(context, matchedRules);
        var scoreBreakdown = CalculateGroupedScoreBreakdown(context, matchedRules);
        var score = scoreBreakdown.Total;
        var level = hasCriticalMatch ? AppPermissionRiskLevel.Critical
            : score >= context.DangerousScoreThreshold ? AppPermissionRiskLevel.Dangerous
            : AppPermissionRiskLevel.Safe;
        return CreateAnalysis(level, context, matchedRules, score, rawScore, scoreBreakdown);
    }

    private static AppPermissionRiskAnalysis CreateAnalysis(
        AppPermissionRiskLevel level,
        AnalysisContext context,
        IReadOnlyList<MatchedRule> matchedRules,
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
            context.GetConfidence(matchedRules),
            scoreBreakdown,
            context.GetManifestPermissions(),
            context.GetRuntimePermissions())
        {
            Findings = matchedRules.Select(match => match.Rule.CreateFinding(context, match.ScoreBreakdown.Total)).ToArray(),
            UnavailableChecks = context.GetUnavailableChecks()
        };
    }

    private static string[] GetMatchedRuleIds(IReadOnlyList<MatchedRule> matchedRules)
    {
        var ruleIds = new string[matchedRules.Count];
        for (var index = 0; index < matchedRules.Count; index++)
        {
            ruleIds[index] = matchedRules[index].Rule.Id;
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
            context.GetRuntimePermissions())
        {
            UnavailableChecks = context.GetUnavailableChecks()
        };
    }
}
