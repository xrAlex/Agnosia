using Agnosia.Models;

namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private sealed partial class AnalysisContext
    {
        public IReadOnlyList<string> GetUnavailableChecks() =>
            _unavailableChecks ??= BuildUnavailableChecks();

        private IReadOnlyList<string> BuildUnavailableChecks()
        {
            var unavailable = _orderedPermissions.Where(p =>
                IsRuntimeSensitivePermission(p) && GetGrantStatus(p) == PermissionGrantStatus.Unknown
                || TryGetSpecialAccessState(p, out var state) && state is null).ToHashSet(StringComparer.Ordinal);
            foreach (var permission in UnavailableAppOpPermissions.Where(HasGrantedPermission)) unavailable.Add(permission);
            if (HasPermission(WriteExternalStorage) && HasGrantedPermission(WriteExternalStorage)
                && TargetSdkVersion is > 0 and <= LegacyExternalStorageMaxTargetSdk
                && HasLegacyExternalStorageAccess is null) unavailable.Add(WriteExternalStorage);
            // On Android 12–13, Health Connect grants live in the companion provider.
            // Package permission flags cannot establish that provider's access state.
            if (DeviceSdkVersion < Android14Api)
                foreach (var permission in _orderedPermissions.Where(p => p.StartsWith("android.permission.health.", StringComparison.Ordinal)))
                    unavailable.Add(permission);
            if ((HasForegroundServiceType(FgsMediaProjection) || HasPermission(ForegroundServiceMediaProjection))
                && IsMediaProjectionActive is null && !HasObservedSignal(ObservedMediaProjection)) unavailable.Add(ObservedMediaProjection);
            if (DeviceSdkVersion == Android16Api && HasPermission(NearbyWifiDevices) && IsLocalNetworkRestrictionEnabled is null)
                unavailable.Add("android.observed.LocalNetworkRestriction");
            return unavailable.Order(StringComparer.Ordinal).ToArray();
        }

        public AppPermissionRiskConfidence GetConfidence(IReadOnlyList<MatchedRule> matchedRules) =>
            GetUnavailableChecks().Count == 0 && HasRelevantConfirmedSignal(matchedRules)
                ? AppPermissionRiskConfidence.High : AppPermissionRiskConfidence.Medium;

        private bool HasRelevantConfirmedSignal(IReadOnlyList<MatchedRule> rules)
        {
            foreach (var match in rules)
            {
                var rule = match.Rule;
                if (rule.RequiredPermissions.Any(permission =>
                        IsRuntimeSensitivePermission(permission) && HasEffectivePermission(permission)
                        || HasEnabledControlSurface(permission))) return true;
                if (rule.RequiredObservedSignals.Any(HasObservedSignal)) return true;
                if (rule.RequiredPermissionPrefixes.Any(prefix =>
                        _orderedPermissions.Any(permission => permission.StartsWith(prefix, StringComparison.Ordinal)
                            && IsRuntimeSensitivePermission(permission) && HasEffectivePermission(permission)))) return true;
            }
            return false;
        }

        public AppPermissionRiskEvidenceState GetEvidenceState(string permission)
        {
            if (HasEnabledControlSurface(permission)) return AppPermissionRiskEvidenceState.Enabled;
            if (IsBlockedByAppOp(permission) || GetGrantStatus(permission) == PermissionGrantStatus.Denied)
                return AppPermissionRiskEvidenceState.Denied;
            if (TryGetSpecialAccessState(permission, out var specialState) && specialState == false)
                return AppPermissionRiskEvidenceState.Disabled;
            if (HasEffectivePermission(permission) && IsForegroundOnly(permission))
                return AppPermissionRiskEvidenceState.ForegroundOnly;
            if (GetGrantStatus(permission) == PermissionGrantStatus.Granted)
                return AppPermissionRiskEvidenceState.Granted;
            if (IsRuntimeSensitivePermission(permission) ||
                (TryGetSpecialAccessState(permission, out var enabled) && enabled is null))
                return AppPermissionRiskEvidenceState.Unknown;
            return AppPermissionRiskEvidenceState.Declared;
        }

        public IReadOnlyList<string> GetManifestPermissions() => _manifestPermissions;

        public IReadOnlyList<string> GetRuntimePermissions() => HasPermissionGrantState
            ? _manifestPermissions.Where(permission => IsRuntimeSensitivePermission(permission)
                                                    && HasEffectivePermission(permission)).ToArray()
            : [];

        public bool HasObservedSignal(string signal) =>
            _observedSignals.Contains(signal) || signal switch
            {
                ObservedMediaProjection => IsMediaProjectionActive == true,
                ObservedVpnControl => IsVpnControlEnabled == true,
                ObservedAssistantScreenContent => IsAssistantScreenContentEnabled == true,
                _ => false
            };

        public AppPermissionRiskEvidenceState GetObservedEvidenceState(string signal) =>
            signal is ObservedDefaultSmsRole or ObservedDefaultDialerRole or ObservedAssistantRole
                ? AppPermissionRiskEvidenceState.Enabled
                : _observedSignals.Contains(signal) || signal == ObservedMediaProjection && IsMediaProjectionActive == true
                ? AppPermissionRiskEvidenceState.Observed
                : AppPermissionRiskEvidenceState.Enabled;

        public IReadOnlyList<string> GetRiskyPermissions(IReadOnlyList<MatchedRule> matchedRules)
        {
            var relevantPermissions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in matchedRules)
            {
                var rule = match.Rule;
                foreach (var permission in rule.RequiredPermissions) relevantPermissions.Add(permission);
                foreach (var prefix in rule.RequiredPermissionPrefixes)
                    foreach (var permission in _orderedPermissions.Where(permission =>
                                 permission.StartsWith(prefix, StringComparison.Ordinal) && HasEffectivePermission(permission)))
                        relevantPermissions.Add(permission);

                if (rule.ForegroundServiceType is null) continue;
                relevantPermissions.Add(ForegroundService);
                if (ForegroundServicePermissionByType.TryGetValue(rule.ForegroundServiceType, out var fgsPermission))
                    relevantPermissions.Add(fgsPermission);
            }

            foreach (var permission in GetDeclaredExfiltrationPermissions()) relevantPermissions.Add(permission);
            return _orderedPermissions.Where(relevantPermissions.Contains).ToArray();
        }
    }
}
