namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private enum PermissionGrantStatus
    {
        Unknown,
        Granted,
        Denied
    }

    private sealed partial class AnalysisContext
    {
        private readonly HashSet<string> _permissions;
        private readonly HashSet<string> _grantedPermissions;
        private readonly HashSet<string> _deniedPermissions;
        private readonly HashSet<string> _foregroundServiceTypes;
        private readonly HashSet<string> _observedSignals;
        private readonly IReadOnlyList<string> _manifestPermissions;
        private readonly IReadOnlyList<string> _orderedPermissions;
        private IReadOnlyList<string>? _unavailableChecks;
        private IReadOnlyList<string>? _exfiltrationPermissions;

        public int DeviceSdkVersion { get; }

        public int TargetSdkVersion { get; }

        public bool? IsLocalNetworkRestrictionEnabled { get; private init; }

        private HashSet<string> ForegroundOnlyPermissions { get; init; } = [];
        private HashSet<string> UnavailableAppOpPermissions { get; init; } = [];
        private HashSet<string> BlockedAppOpPermissions { get; init; } = [];
        private bool? IsInputMethodEnabled { get; init; }
        private bool? IsAutofillServiceEnabled { get; init; }
        private bool? IsDeviceAdminEnabled { get; init; }
        private bool? HasLegacyExternalStorageAccess { get; init; }

        public bool IsForegroundOnly(string permission) => ForegroundOnlyPermissions.Contains(permission);

        public bool HasHealthDataAccess => GetPermissionsByPrefix(HealthReadPrefix)
            .Any(p => p is not HealthBackground and not HealthHistory && HasEffectivePermission(p));

        public bool HasBackgroundHealthDataAccess => GetPermissionsByPrefix(HealthReadPrefix)
            .Any(p => p is not HealthBackground and not HealthHistory
                      && HasEffectivePermission(p) && !IsForegroundOnly(p));

        public bool? IsAccessibilityServiceEnabled { get; }

        public bool? IsNotificationListenerEnabled { get; }

        public bool? CanDrawOverlays { get; }

        public bool? HasUsageStatsAccess { get; }

        public bool? IsVpnControlEnabled { get; }

        public bool? IsAssistantScreenContentEnabled { get; }

        public bool? IsMediaProjectionActive { get; }

        public bool? IsCameraAppOpAllowed { get; }

        public bool? IsMicrophoneAppOpAllowed { get; }

        public bool? IsFineLocationAppOpAllowed { get; }

        public bool? IsCoarseLocationAppOpAllowed { get; }

        public bool? HasManageExternalStorageAccess { get; }

        public bool? CanRequestPackageInstalls { get; }

        public bool? CanScheduleExactAlarms { get; }

        public bool? IsIgnoringBatteryOptimizations { get; }

        public bool HasAnySignal =>
            _orderedPermissions.Count > 0
            || _foregroundServiceTypes.Count > 0
            || _observedSignals.Count > 0
            || IsAccessibilityServiceEnabled == true
            || IsNotificationListenerEnabled == true
            || CanDrawOverlays == true
            || HasUsageStatsAccess == true
            || IsVpnControlEnabled == true
            || IsAssistantScreenContentEnabled == true
            || IsMediaProjectionActive == true
            || IsCameraAppOpAllowed is not null
            || IsMicrophoneAppOpAllowed is not null
            || IsFineLocationAppOpAllowed is not null
            || IsCoarseLocationAppOpAllowed is not null
            || HasManageExternalStorageAccess == true
            || CanRequestPackageInstalls == true
            || CanScheduleExactAlarms == true
            || IsIgnoringBatteryOptimizations == true;

        public bool HasPermissionGrantState => _grantedPermissions.Count > 0 || _deniedPermissions.Count > 0;

        public int DangerousScoreThreshold => BaseDangerousScoreThreshold;
    }
}
