using Agnosia.Models;

namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private static PermissionCombinationRule Rule(
        string id,
        AppPermissionRiskLevel level,
        string[] requiredPermissions,
        int minDeviceSdkVersion = Android12Api,
        int? maxDeviceSdkVersion = null,
        int? minTargetSdkVersion = null,
        int? maxTargetSdkVersion = null,
        string? foregroundServiceType = null,
        string[]? excludedPermissions = null,
        string[]? requiredPermissionPrefixes = null,
        string[]? requiredObservedSignals = null,
        bool requireExfiltrationChannel = false,
        bool requireEffectivePermissionsForMatch = false,
        int score = BaseDangerousScoreThreshold,
        Func<AnalysisContext, bool>? extraCondition = null)
    {
        return Rule(
            id,
            GetCapabilityGroup(id),
            level,
            requiredPermissions,
            minDeviceSdkVersion,
            maxDeviceSdkVersion,
            minTargetSdkVersion,
            maxTargetSdkVersion,
            foregroundServiceType,
            excludedPermissions,
            requiredPermissionPrefixes,
            requiredObservedSignals,
            requireExfiltrationChannel,
            requireEffectivePermissionsForMatch,
            score,
            extraCondition);
    }

    private static PermissionCombinationRule Rule(
        string id,
        string groupId,
        AppPermissionRiskLevel level,
        string[] requiredPermissions,
        int minDeviceSdkVersion = Android12Api,
        int? maxDeviceSdkVersion = null,
        int? minTargetSdkVersion = null,
        int? maxTargetSdkVersion = null,
        string? foregroundServiceType = null,
        string[]? excludedPermissions = null,
        string[]? requiredPermissionPrefixes = null,
        string[]? requiredObservedSignals = null,
        bool requireExfiltrationChannel = false,
        bool requireEffectivePermissionsForMatch = false,
        int score = BaseDangerousScoreThreshold,
        Func<AnalysisContext, bool>? extraCondition = null)
    {
        return new PermissionCombinationRule(
            id,
            groupId,
            level,
            minDeviceSdkVersion,
            maxDeviceSdkVersion,
            minTargetSdkVersion,
            maxTargetSdkVersion,
            requiredPermissions,
            excludedPermissions ?? [],
            requiredPermissionPrefixes ?? [],
            requiredObservedSignals ?? [],
            foregroundServiceType,
            requireExfiltrationChannel,
            requireEffectivePermissionsForMatch,
            score,
            extraCondition);
    }

    private sealed record PermissionCombinationRule(
        string Id,
        string GroupId,
        AppPermissionRiskLevel Level,
        int MinDeviceSdkVersion,
        int? MaxDeviceSdkVersion,
        int? MinTargetSdkVersion,
        int? MaxTargetSdkVersion,
        IReadOnlyList<string> RequiredPermissions,
        IReadOnlyList<string> ExcludedPermissions,
        IReadOnlyList<string> RequiredPermissionPrefixes,
        IReadOnlyList<string> RequiredObservedSignals,
        string? ForegroundServiceType,
        bool RequireExfiltrationChannel,
        bool RequireEffectivePermissionsForMatch,
        int Score,
        Func<AnalysisContext, bool>? ExtraCondition)
    {
        public AppPermissionRiskFinding CreateFinding(AnalysisContext context, int ruleScore)
        {
            var evidence = new List<AppPermissionRiskEvidence>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(string id, AppPermissionRiskEvidenceState state)
            {
                if (seen.Add(id)) evidence.Add(new(id, state));
            }

            foreach (var permission in RequiredPermissions)
                Add(permission, context.GetEvidenceState(permission));
            foreach (var prefix in RequiredPermissionPrefixes)
                foreach (var permission in context.GetPermissionsByPrefix(prefix))
                    if (context.HasEffectivePermission(permission))
                        Add(permission, context.GetEvidenceState(permission));
            if (ForegroundServiceType is not null)
                Add("android.foregroundServiceType." + ForegroundServiceType, AppPermissionRiskEvidenceState.Declared);
            foreach (var signal in RequiredObservedSignals)
                Add(signal, context.GetObservedEvidenceState(signal));
            // A channel belongs to this finding only when the rule requires it.
            // Explicit channels are already included through RequiredPermissions.
            if (RequireExfiltrationChannel)
                foreach (var permission in context.GetDeclaredExfiltrationPermissions())
                    Add(permission, context.GetEvidenceState(permission));

            var exfiltrationScore = context.GetExfiltrationScore(RequireExfiltrationChannel ? null : RequiredPermissions);
            var level = Level == AppPermissionRiskLevel.Critical ||
                        ruleScore + exfiltrationScore >= BaseDangerousScoreThreshold
                ? Level : AppPermissionRiskLevel.Safe;
            return new AppPermissionRiskFinding(Id, level, evidence);
        }

        public bool IsMatch(AnalysisContext context)
        {
            return MatchesSdk(context)
                   && HasRequiredPermissions(context)
                   && HasNoExcludedPermissions(context)
                   && HasRequiredPermissionPrefixes(context)
                   && HasRequiredObservedSignals(context)
                   && HasRequiredForegroundServiceType(context)
                   && HasRequiredExfiltrationChannel(context)
                   && ExtraCondition?.Invoke(context) != false;
        }

        public bool IsCriticalMatch(AnalysisContext context)
        {
            return IsMatch(context)
                   && RequiredPermissions.All(context.HasEffectivePermission)
                   && !RequiredPermissions.Any(context.IsForegroundOnly)
                   && RequiredPermissionPrefixes.All(context.HasEffectivePermissionPrefix);
        }

        public AppPermissionRiskScoreBreakdown GetScoreBreakdown(AnalysisContext context)
        {
            var score = Score;
            var legitimacyPenalty = 0;
            var hasRuntimeSensitivePermission = false;
            var hasDeniedRuntimeSensitivePermission = false;
            var isBlockedByAppOp = false;
            var controlSurfaceScore = 0;
            foreach (var permission in RequiredPermissions)
            {
                if (context.IsRuntimeSensitivePermission(permission))
                {
                    hasRuntimeSensitivePermission = true;
                    if (context.HasPermissionGrantState
                        && context.GetGrantStatus(permission) == PermissionGrantStatus.Denied)
                        hasDeniedRuntimeSensitivePermission = true;
                }

                if (context.IsBlockedByAppOp(permission)) isBlockedByAppOp = true;
                if (controlSurfaceScore == 0 && context.HasEnabledControlSurface(permission))
                    controlSurfaceScore = 1;
            }

            if (context.HasPermissionGrantState
                && hasRuntimeSensitivePermission
                && hasDeniedRuntimeSensitivePermission)
            {
                legitimacyPenalty += 2;
            }

            if (isBlockedByAppOp) legitimacyPenalty += 2;

            return new AppPermissionRiskScoreBreakdown(
                score,
                context.GetPersistenceScore(RequiredPermissions, ForegroundServiceType),
                0,
                controlSurfaceScore,
                context.GetStealthScore(RequiredPermissions),
                legitimacyPenalty,
                0);
        }

        private bool MatchesSdk(AnalysisContext context)
        {
            if (context.DeviceSdkVersion < MinDeviceSdkVersion) return false;
            if (MaxDeviceSdkVersion is not null && context.DeviceSdkVersion > MaxDeviceSdkVersion) return false;
            if (MinTargetSdkVersion is not null && context.TargetSdkVersion < MinTargetSdkVersion) return false;

            return MaxTargetSdkVersion is null
                   || (context.TargetSdkVersion != 0 && context.TargetSdkVersion <= MaxTargetSdkVersion);
        }

        private bool HasRequiredPermissions(AnalysisContext context)
        {
            Func<string, bool> hasPermission = RequireEffectivePermissionsForMatch
                ? context.HasEffectivePermission
                : context.HasPermission;

            return RequiredPermissions.All(hasPermission);
        }

        private bool HasNoExcludedPermissions(AnalysisContext context)
        {
            return !ExcludedPermissions.Any(context.HasEffectivePermission);
        }

        private bool HasRequiredPermissionPrefixes(AnalysisContext context)
        {
            return RequiredPermissionPrefixes.All(RequireEffectivePermissionsForMatch
                ? context.HasEffectivePermissionPrefix : context.HasPermissionPrefix);
        }

        private bool HasRequiredObservedSignals(AnalysisContext context)
        {
            return RequiredObservedSignals.All(context.HasObservedSignal);
        }

        private bool HasRequiredForegroundServiceType(AnalysisContext context)
        {
            return ForegroundServiceType is null
                   || context.HasForegroundServiceType(ForegroundServiceType);
        }

        private bool HasRequiredExfiltrationChannel(AnalysisContext context)
        {
            return !RequireExfiltrationChannel || context.HasExfiltrationChannel;
        }
    }

    private static string GetCapabilityGroup(string id) => id switch
    {
        var s when s.StartsWith("CR-MIC-", StringComparison.Ordinal) => "microphone",
        var s when s.StartsWith("CR-CAM-", StringComparison.Ordinal) => "camera",
        "CR-SCR-01" or "SU-SCR-FGS-01" => "screen",
        var s when s.Contains("SMS-", StringComparison.Ordinal) => "sms",
        var s when s.Contains("CALL-LOG", StringComparison.Ordinal) => "call-log",
        var s when s.StartsWith("CR-PROF-", StringComparison.Ordinal) => "usage",
        "SU-PROF-USAGE-01" => "usage",
        var s when s.StartsWith("CR-FILE-ALL-", StringComparison.Ordinal) => "all-files",
        "SU-FILE-ALL-01" => "all-files",
        var s when s.StartsWith("CR-UI-", StringComparison.Ordinal) => "interface-control",
        "SU-UI-ACC-01" or "SU-UI-OVERLAY-01" or "SU-NOTIF-01" or "SU-NOTIF-OVERLAY-01" => "interface-control",
        _ => id
    };
}
