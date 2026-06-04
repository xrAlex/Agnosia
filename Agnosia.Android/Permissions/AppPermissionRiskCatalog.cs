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
        var score = CalculateGroupedScore(context, matchedRules);

        if (hasCriticalMatch)
            return CreateAnalysis(
                AppPermissionRiskLevel.Critical,
                context,
                matchedRules,
                score,
                rawScore);

        if (matchedRules.Count == 0 || score < context.DangerousScoreThreshold)
            return CreateSafeAnalysis(context);

        if (score >= CriticalScoreThreshold && context.HasHighConfidenceSignals)
            return CreateAnalysis(
                AppPermissionRiskLevel.Critical,
                context,
                matchedRules,
                score,
                rawScore);

        return CreateAnalysis(
            AppPermissionRiskLevel.Dangerous,
            context,
            matchedRules,
            score,
            rawScore);
    }

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
            id,
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

    private static AppPermissionRiskAnalysis CreateAnalysis(
        AppPermissionRiskLevel level,
        AnalysisContext context,
        IReadOnlyList<PermissionCombinationRule> matchedRules,
        int score,
        int rawScore)
    {
        return new AppPermissionRiskAnalysis(
            level,
            context.GetRiskyPermissions(matchedRules),
            GetMatchedRuleIds(matchedRules),
            score,
            rawScore,
            context.GetConfidence(),
            CalculateGroupedScoreBreakdown(context, matchedRules),
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

    private static int CalculateRawScore(
        AnalysisContext context,
        IReadOnlyList<PermissionCombinationRule> matchedRules)
    {
        var score = 0;
        for (var index = 0; index < matchedRules.Count; index++)
        {
            score += matchedRules[index].GetScore(context);
        }

        return score;
    }

    private static int CalculateGroupedScore(
        AnalysisContext context,
        IReadOnlyList<PermissionCombinationRule> matchedRules)
    {
        var scoreByGroup = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < matchedRules.Count; index++)
        {
            var rule = matchedRules[index];
            var score = rule.GetScore(context);
            if (!scoreByGroup.TryGetValue(rule.GroupId, out var currentScore) || score > currentScore)
                scoreByGroup[rule.GroupId] = score;
        }

        var total = 0;
        foreach (var score in scoreByGroup.Values) total += score;

        return total;
    }

    private static AppPermissionRiskScoreBreakdown CalculateGroupedScoreBreakdown(
        AnalysisContext context,
        IReadOnlyList<PermissionCombinationRule> matchedRules)
    {
        var breakdownByGroup = new Dictionary<string, AppPermissionRiskScoreBreakdown>(StringComparer.Ordinal);
        for (var index = 0; index < matchedRules.Count; index++)
        {
            var rule = matchedRules[index];
            var breakdown = rule.GetScoreBreakdown(context);
            if (!breakdownByGroup.TryGetValue(rule.GroupId, out var currentBreakdown)
                || breakdown.Total > currentBreakdown.Total)
                breakdownByGroup[rule.GroupId] = breakdown;
        }

        return SumBreakdowns(breakdownByGroup.Values);
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
                   && RequiredPermissionPrefixes.All(context.HasEffectivePermissionPrefix);
        }

        public int GetScore(AnalysisContext context)
        {
            return GetScoreBreakdown(context).Total;
        }

        public AppPermissionRiskScoreBreakdown GetScoreBreakdown(AnalysisContext context)
        {
            var score = Score;
            var legitimacyPenalty = 0;
            var hasRuntimeSensitivePermission = false;
            var hasDeniedRuntimeSensitivePermission = false;
            var isBlockedByAppOp = false;
            var controlSurfaceScore = 0;
            for (var index = 0; index < RequiredPermissions.Count; index++)
            {
                var permission = RequiredPermissions[index];
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
                context.GetPersistenceScore(RequiredPermissions),
                context.GetExfiltrationScore(),
                controlSurfaceScore,
                context.GetStealthScore(RequiredPermissions),
                legitimacyPenalty,
                context.GetConfidenceScore());
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
            return !ExcludedPermissions.Any(context.HasPermission);
        }

        private bool HasRequiredPermissionPrefixes(AnalysisContext context)
        {
            return RequiredPermissionPrefixes.All(context.HasPermissionPrefix);
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

    private enum PermissionGrantStatus
    {
        Unknown,
        Granted,
        Denied
    }

    private sealed class AnalysisContext
    {
        private readonly HashSet<string> _permissions;
        private readonly HashSet<string> _grantedPermissions;
        private readonly HashSet<string> _deniedPermissions;
        private readonly HashSet<string> _foregroundServiceTypes;
        private readonly HashSet<string> _observedSignals;
        private readonly IReadOnlyList<string> _manifestPermissions;
        private readonly IReadOnlyList<string> _orderedPermissions;

        private AnalysisContext(
            int deviceSdkVersion,
            int targetSdkVersion,
            bool isAccessibilityServiceEnabled,
            bool isNotificationListenerEnabled,
            bool canDrawOverlays,
            bool hasUsageStatsAccess,
            bool isVpnControlEnabled,
            bool isAssistantScreenContentEnabled,
            bool isMediaProjectionActive,
            bool? isCameraAppOpAllowed,
            bool? isMicrophoneAppOpAllowed,
            bool? isFineLocationAppOpAllowed,
            bool? isCoarseLocationAppOpAllowed,
            IReadOnlyList<string> orderedPermissions,
            HashSet<string> permissions,
            HashSet<string> grantedPermissions,
            HashSet<string> deniedPermissions,
            HashSet<string> foregroundServiceTypes,
            HashSet<string> observedSignals,
            IReadOnlyList<string> manifestPermissions)
        {
            DeviceSdkVersion = deviceSdkVersion;
            TargetSdkVersion = targetSdkVersion;
            IsAccessibilityServiceEnabled = isAccessibilityServiceEnabled;
            IsNotificationListenerEnabled = isNotificationListenerEnabled;
            CanDrawOverlays = canDrawOverlays;
            HasUsageStatsAccess = hasUsageStatsAccess;
            IsVpnControlEnabled = isVpnControlEnabled;
            IsAssistantScreenContentEnabled = isAssistantScreenContentEnabled;
            IsMediaProjectionActive = isMediaProjectionActive;
            IsCameraAppOpAllowed = isCameraAppOpAllowed;
            IsMicrophoneAppOpAllowed = isMicrophoneAppOpAllowed;
            IsFineLocationAppOpAllowed = isFineLocationAppOpAllowed;
            IsCoarseLocationAppOpAllowed = isCoarseLocationAppOpAllowed;
            _orderedPermissions = orderedPermissions;
            _permissions = permissions;
            _grantedPermissions = grantedPermissions;
            _deniedPermissions = deniedPermissions;
            _foregroundServiceTypes = foregroundServiceTypes;
            _observedSignals = observedSignals;
            _manifestPermissions = manifestPermissions;
        }

        public int DeviceSdkVersion { get; }

        public int TargetSdkVersion { get; }

        public bool IsAccessibilityServiceEnabled { get; }

        public bool IsNotificationListenerEnabled { get; }

        public bool CanDrawOverlays { get; }

        public bool HasUsageStatsAccess { get; }

        public bool IsVpnControlEnabled { get; }

        public bool IsAssistantScreenContentEnabled { get; }

        public bool IsMediaProjectionActive { get; }

        public bool? IsCameraAppOpAllowed { get; }

        public bool? IsMicrophoneAppOpAllowed { get; }

        public bool? IsFineLocationAppOpAllowed { get; }

        public bool? IsCoarseLocationAppOpAllowed { get; }

        public bool HasAnySignal =>
            _orderedPermissions.Count > 0
            || _foregroundServiceTypes.Count > 0
            || _observedSignals.Count > 0
            || IsAccessibilityServiceEnabled
            || IsNotificationListenerEnabled
            || CanDrawOverlays
            || HasUsageStatsAccess
            || IsVpnControlEnabled
            || IsAssistantScreenContentEnabled
            || IsMediaProjectionActive
            || IsCameraAppOpAllowed is not null
            || IsMicrophoneAppOpAllowed is not null
            || IsFineLocationAppOpAllowed is not null
            || IsCoarseLocationAppOpAllowed is not null;

        public bool HasPermissionGrantState => _grantedPermissions.Count > 0 || _deniedPermissions.Count > 0;

        public bool HasHighConfidenceSignals =>
            HasPermissionGrantState
            || IsAccessibilityServiceEnabled
            || IsNotificationListenerEnabled
            || CanDrawOverlays
            || HasUsageStatsAccess
            || IsVpnControlEnabled
            || IsAssistantScreenContentEnabled
            || IsMediaProjectionActive
            || IsCameraAppOpAllowed == true
            || IsMicrophoneAppOpAllowed == true
            || IsFineLocationAppOpAllowed == true
            || IsCoarseLocationAppOpAllowed == true;

        public int DangerousScoreThreshold => BaseDangerousScoreThreshold;

        public AppPermissionRiskConfidence GetConfidence()
        {
            return HasHighConfidenceSignals
                ? AppPermissionRiskConfidence.High
                : AppPermissionRiskConfidence.Medium;
        }

        public static AnalysisContext Create(AppPermissionRiskInput input)
        {
            var requestedPermissions = NormalizeDistinct(input.RequestedPermissions);
            var servicePermissions = NormalizeDistinct(input.ServicePermissions);
            var grantedPermissions = NormalizeDistinctSet(input.GrantedPermissions);
            var deniedPermissions = NormalizeDistinctSet(input.DeniedPermissions);
            var orderedPermissions = new List<string>(requestedPermissions.Count + servicePermissions.Count);
            var permissions = new HashSet<string>(StringComparer.Ordinal);
            AddDistinct(orderedPermissions, permissions, requestedPermissions);
            AddDistinct(orderedPermissions, permissions, servicePermissions);
            var manifestPermissions = orderedPermissions.ToArray();
            AddInferredSpecialAccessPermissions(input, orderedPermissions, permissions);

            return new AnalysisContext(
                NormalizeDeviceSdkVersion(input.DeviceSdkVersion),
                input.TargetSdkVersion,
                input.IsAccessibilityServiceEnabled,
                input.IsNotificationListenerEnabled,
                input.CanDrawOverlays,
                input.HasUsageStatsAccess,
                input.IsVpnControlEnabled,
                input.IsAssistantScreenContentEnabled,
                input.IsMediaProjectionActive,
                input.IsCameraAppOpAllowed,
                input.IsMicrophoneAppOpAllowed,
                input.IsFineLocationAppOpAllowed,
                input.IsCoarseLocationAppOpAllowed,
                orderedPermissions,
                permissions,
                grantedPermissions,
                deniedPermissions,
                NormalizeDistinctSet(input.ForegroundServiceTypes),
                NormalizeDistinctSet(input.ObservedSignals),
                manifestPermissions);
        }

        public bool HasPermission(string permission)
        {
            return _permissions.Contains(permission);
        }

        public bool HasPermissionPrefix(string prefix)
        {
            return _orderedPermissions.Any(permission => permission.StartsWith(prefix, StringComparison.Ordinal));
        }

        public IReadOnlyList<string> GetManifestPermissions()
        {
            return _manifestPermissions;
        }

        public IReadOnlyList<string> GetRuntimePermissions()
        {
            if (HasPermissionGrantState)
                return _manifestPermissions
                    .Where(permission => IsRuntimeSensitivePermission(permission) && HasGrantedPermission(permission))
                    .ToArray();

            return _manifestPermissions
                .Where(IsRuntimeSensitivePermission)
                .ToArray();
        }

        public bool HasObservedSignal(string signal)
        {
            return _observedSignals.Contains(signal)
                   || signal switch
                   {
                       ObservedMediaProjection => IsMediaProjectionActive,
                       ObservedVpnControl => IsVpnControlEnabled,
                       ObservedAssistantScreenContent => IsAssistantScreenContentEnabled,
                       _ => false
                   };
        }

        public bool HasEffectivePermission(string permission)
        {
            if (!HasPermission(permission)) return false;

            if (TryGetSpecialAccessState(permission, out var specialAccessState))
                return specialAccessState;

            if (IsBlockedByAppOp(permission)) return false;

            if (IsRuntimeSensitivePermission(permission))
                return GetGrantStatus(permission) == PermissionGrantStatus.Granted
                       || IsAllowedByAppOp(permission);

            return true;
        }

        public bool HasEffectivePermissionPrefix(string prefix)
        {
            return _orderedPermissions.Any(permission =>
                permission.StartsWith(prefix, StringComparison.Ordinal)
                && HasEffectivePermission(permission));
        }

        public bool HasGrantedPermission(string permission)
        {
            return _grantedPermissions.Contains(permission);
        }

        public bool HasDeniedPermission(string permission)
        {
            return _deniedPermissions.Contains(permission);
        }

        public PermissionGrantStatus GetGrantStatus(string permission)
        {
            if (HasDeniedPermission(permission)) return PermissionGrantStatus.Denied;
            if (HasGrantedPermission(permission)) return PermissionGrantStatus.Granted;

            return PermissionGrantStatus.Unknown;
        }

        public bool IsRuntimeSensitivePermission(string permission)
        {
            return permission is AccessBackgroundLocation
                       or AccessCoarseLocation
                       or AccessFineLocation
                       or AccessMediaLocation
                       or AccessLocalNetwork
                       or AnswerPhoneCalls
                       or BluetoothConnect
                       or BluetoothScan
                       or Camera
                       or NearbyWifiDevices
                       or PostNotifications
                       or Ranging
                       or ReadCallLog
                       or ReadContacts
                       or ReadExternalStorage
                       or ReadMediaAudio
                       or ReadMediaImages
                       or ReadMediaVisualUserSelected
                       or ReadMediaVideo
                       or ReadPhoneNumbers
                       or ReadPhoneState
                       or ReadSms
                       or ReceiveSms
                       or RecordAudio
                       or SendSms
                       or WriteCallLog
                       or WriteExternalStorage;
        }

        public bool HasEnabledControlSurface(string permission)
        {
            return TryGetSpecialAccessState(permission, out var isEnabled) && isEnabled;
        }

        private bool TryGetSpecialAccessState(string permission, out bool isEnabled)
        {
            isEnabled = permission switch
            {
                BindAccessibilityService => IsAccessibilityServiceEnabled,
                BindNotificationListenerService => IsNotificationListenerEnabled,
                BindVpnService => IsVpnControlEnabled,
                ReadAssistStructureScreenContent => IsAssistantScreenContentEnabled,
                SystemAlertWindow => CanDrawOverlays,
                PackageUsageStats => HasUsageStatsAccess,
                _ => false
            };

            return permission is BindAccessibilityService
                or BindNotificationListenerService
                or BindVpnService
                or ReadAssistStructureScreenContent
                or SystemAlertWindow
                or PackageUsageStats;
        }

        public bool IsBlockedByAppOp(string permission)
        {
            return permission switch
            {
                Camera => IsCameraAppOpAllowed == false,
                RecordAudio => IsMicrophoneAppOpAllowed == false,
                AccessFineLocation => IsFineLocationAppOpAllowed == false,
                AccessCoarseLocation => IsCoarseLocationAppOpAllowed == false,
                _ => false
            };
        }

        private bool IsAllowedByAppOp(string permission)
        {
            return permission switch
            {
                Camera => IsCameraAppOpAllowed == true,
                RecordAudio => IsMicrophoneAppOpAllowed == true,
                AccessFineLocation => IsFineLocationAppOpAllowed == true,
                AccessCoarseLocation => IsCoarseLocationAppOpAllowed == true,
                _ => false
            };
        }

        public bool HasForegroundServiceType(string type)
        {
            return _foregroundServiceTypes.Contains(type)
                   || (ForegroundServicePermissionByType.TryGetValue(type, out var permission)
                       && HasPermission(permission));
        }

        public bool HasExfiltrationChannel =>
            HasPermission(Internet)
            || HasPermission(AccessLocalNetwork)
            || HasPermission(NearbyWifiDevices)
            || HasPermission(BluetoothConnect)
            || HasPermission(BluetoothScan)
            || HasPermission(Nfc)
            || HasPermission(Ranging)
            || HasPermission(SendSms)
            || HasPermission(WriteExternalStorage)
            || HasPermission(ManageExternalStorage);

        public int GetExfiltrationScore()
        {
            var score = 0;
            if (HasPermission(Internet)) score += 2;
            if (HasPermission(AccessLocalNetwork)) score += 2;
            if (HasPermission(NearbyWifiDevices)) score += 1;
            if (HasPermission(BluetoothConnect) || HasPermission(BluetoothScan)) score += 1;
            if (HasPermission(Nfc)) score += 1;
            if (HasPermission(Ranging)) score += 1;
            if (HasPermission(SendSms)) score += 1;
            if (HasPermission(WriteExternalStorage) || HasPermission(ManageExternalStorage)) score += 1;

            return score;
        }

        public int GetPersistenceScore(IReadOnlyList<string> permissions)
        {
            var score = 0;
            if (ContainsPermission(permissions, BootCompleted)) score += 2;
            if (ContainsPermission(permissions, ScheduleExactAlarm) || ContainsPermission(permissions, UseExactAlarm))
                score += 1;
            if (ContainsPermission(permissions, ForegroundService) || _foregroundServiceTypes.Count > 0) score += 1;

            return score;
        }

        public int GetStealthScore(IReadOnlyList<string> permissions)
        {
            return ContainsPermission(permissions, IgnoreBatteryOptimizations) ? 2 : 0;
        }

        public int GetConfidenceScore()
        {
            return HasHighConfidenceSignals || _observedSignals.Count > 0 ? 1 : 0;
        }

        public IReadOnlyList<string> GetRiskyPermissions(IReadOnlyList<PermissionCombinationRule> matchedRules)
        {
            var relevantPermissions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in matchedRules)
            {
                foreach (var permission in rule.RequiredPermissions) relevantPermissions.Add(permission);
                foreach (var prefix in rule.RequiredPermissionPrefixes)
                {
                    foreach (var permission in _orderedPermissions.Where(permission =>
                                 permission.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        relevantPermissions.Add(permission);
                    }
                }

                if (rule.ForegroundServiceType is null) continue;

                relevantPermissions.Add(ForegroundService);
                if (ForegroundServicePermissionByType.TryGetValue(rule.ForegroundServiceType, out var fgsPermission))
                    relevantPermissions.Add(fgsPermission);
            }

            foreach (var permission in GetDeclaredExfiltrationPermissions())
            {
                relevantPermissions.Add(permission);
            }

            return _orderedPermissions
                .Where(relevantPermissions.Contains)
                .ToArray();
        }

        private IEnumerable<string> GetDeclaredExfiltrationPermissions()
        {
            if (HasPermission(Internet)) yield return Internet;
            if (HasPermission(AccessLocalNetwork)) yield return AccessLocalNetwork;
            if (HasPermission(NearbyWifiDevices)) yield return NearbyWifiDevices;
            if (HasPermission(BluetoothConnect)) yield return BluetoothConnect;
            if (HasPermission(BluetoothScan)) yield return BluetoothScan;
            if (HasPermission(Nfc)) yield return Nfc;
            if (HasPermission(Ranging)) yield return Ranging;
            if (HasPermission(SendSms)) yield return SendSms;
            if (HasPermission(WriteExternalStorage)) yield return WriteExternalStorage;
            if (HasPermission(ManageExternalStorage)) yield return ManageExternalStorage;
        }

        private static bool ContainsPermission(IReadOnlyList<string> permissions, string expected)
        {
            for (var index = 0; index < permissions.Count; index++)
            {
                if (string.Equals(permissions[index], expected, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static int NormalizeDeviceSdkVersion(int value)
        {
            return value > 0 ? value : Android12Api;
        }

        private static List<string> NormalizeDistinct(IEnumerable<string>? values)
        {
            if (values is null) return [];

            var result = values is IReadOnlyCollection<string> collection
                ? new List<string>(collection.Count)
                : new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                var trimmed = value.Trim();
                if (seen.Add(trimmed)) result.Add(trimmed);
            }

            return result;
        }

        private static HashSet<string> NormalizeDistinctSet(IEnumerable<string>? values)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (values is null) return result;

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                result.Add(value.Trim());
            }

            return result;
        }

        private static void AddDistinct(
            List<string> target,
            HashSet<string> seen,
            IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                if (seen.Add(value)) target.Add(value);
            }
        }

        private static void AddDistinct(
            List<string> target,
            HashSet<string> seen,
            string value)
        {
            if (seen.Add(value)) target.Add(value);
        }

        private static void AddInferredSpecialAccessPermissions(
            AppPermissionRiskInput input,
            List<string> orderedPermissions,
            HashSet<string> permissions)
        {
            if (input.IsAccessibilityServiceEnabled)
                AddDistinct(orderedPermissions, permissions, BindAccessibilityService);
            if (input.IsNotificationListenerEnabled)
                AddDistinct(orderedPermissions, permissions, BindNotificationListenerService);
            if (input.CanDrawOverlays)
                AddDistinct(orderedPermissions, permissions, SystemAlertWindow);
            if (input.HasUsageStatsAccess)
                AddDistinct(orderedPermissions, permissions, PackageUsageStats);
            if (input.IsVpnControlEnabled)
                AddDistinct(orderedPermissions, permissions, BindVpnService);
            if (input.IsAssistantScreenContentEnabled)
                AddDistinct(orderedPermissions, permissions, ReadAssistStructureScreenContent);
        }
    }
}
