namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private sealed partial class AnalysisContext
    {
        private AnalysisContext(AppPermissionRiskInput input)
        {
            DeviceSdkVersion = NormalizeDeviceSdkVersion(input.DeviceSdkVersion);
            TargetSdkVersion = input.TargetSdkVersion;
            IsAccessibilityServiceEnabled = input.IsAccessibilityServiceEnabled;
            IsNotificationListenerEnabled = input.IsNotificationListenerEnabled;
            CanDrawOverlays = input.CanDrawOverlays;
            HasUsageStatsAccess = input.HasUsageStatsAccess;
            IsVpnControlEnabled = input.IsVpnControlEnabled;
            IsAssistantScreenContentEnabled = input.IsAssistantScreenContentEnabled;
            IsMediaProjectionActive = input.IsMediaProjectionActive;
            IsCameraAppOpAllowed = input.IsCameraAppOpAllowed;
            IsMicrophoneAppOpAllowed = input.IsMicrophoneAppOpAllowed;
            IsFineLocationAppOpAllowed = input.IsFineLocationAppOpAllowed;
            IsCoarseLocationAppOpAllowed = input.IsCoarseLocationAppOpAllowed;
            HasManageExternalStorageAccess = input.HasManageExternalStorageAccess;
            CanRequestPackageInstalls = input.CanRequestPackageInstalls;
            CanScheduleExactAlarms = input.CanScheduleExactAlarms;
            IsIgnoringBatteryOptimizations = input.IsIgnoringBatteryOptimizations;
            IsLocalNetworkRestrictionEnabled = input.IsLocalNetworkRestrictionEnabled;
            IsInputMethodEnabled = input.IsInputMethodEnabled;
            IsAutofillServiceEnabled = input.IsAutofillServiceEnabled;
            IsDeviceAdminEnabled = input.IsDeviceAdminEnabled;
            HasLegacyExternalStorageAccess = input.HasLegacyExternalStorageAccess;

            var requestedPermissions = NormalizeDistinct(input.RequestedPermissions);
            var servicePermissions = NormalizeDistinct(input.ServicePermissions);
            _grantedPermissions = NormalizeDistinctSet(input.GrantedPermissions);
            _deniedPermissions = NormalizeDistinctSet(input.DeniedPermissions);
            var orderedPermissions = new List<string>(requestedPermissions.Count + servicePermissions.Count);
            _orderedPermissions = orderedPermissions;
            _permissions = new HashSet<string>(StringComparer.Ordinal);
            AddDistinct(orderedPermissions, _permissions, requestedPermissions);
            AddDistinct(orderedPermissions, _permissions, servicePermissions);
            _manifestPermissions = orderedPermissions.ToArray();
            AddInferredSpecialAccessPermissions(input, orderedPermissions, _permissions);

            _foregroundServiceTypes = NormalizeDistinctSet(input.ForegroundServiceTypes);
            _observedSignals = NormalizeDistinctSet(input.ObservedSignals);
            ForegroundOnlyPermissions = NormalizeDistinctSet(input.ForegroundOnlyPermissions);
            UnavailableAppOpPermissions = NormalizeDistinctSet(input.UnavailableAppOpPermissions);
            BlockedAppOpPermissions = NormalizeDistinctSet(input.BlockedAppOpPermissions);
        }

        public static AnalysisContext Create(AppPermissionRiskInput input) => new(input);

        private static int NormalizeDeviceSdkVersion(int value) => value > 0 ? value : Android12Api;

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

        private static void AddDistinct(List<string> target, HashSet<string> seen, IEnumerable<string> values)
        {
            foreach (var value in values)
                if (seen.Add(value)) target.Add(value);
        }

        private static void AddDistinct(List<string> target, HashSet<string> seen, string value)
        {
            if (seen.Add(value)) target.Add(value);
        }

        private static void AddInferredSpecialAccessPermissions(
            AppPermissionRiskInput input,
            List<string> orderedPermissions,
            HashSet<string> permissions)
        {
            if (input.IsInputMethodEnabled == true) AddDistinct(orderedPermissions, permissions, BindInputMethod);
            if (input.IsAutofillServiceEnabled == true) AddDistinct(orderedPermissions, permissions, BindAutofillService);
            if (input.IsDeviceAdminEnabled == true) AddDistinct(orderedPermissions, permissions, BindDeviceAdmin);
            if (input.IsAccessibilityServiceEnabled == true)
                AddDistinct(orderedPermissions, permissions, BindAccessibilityService);
            if (input.IsNotificationListenerEnabled == true)
                AddDistinct(orderedPermissions, permissions, BindNotificationListenerService);
            if (input.CanDrawOverlays == true)
                AddDistinct(orderedPermissions, permissions, SystemAlertWindow);
            if (input.HasUsageStatsAccess == true)
                AddDistinct(orderedPermissions, permissions, PackageUsageStats);
            if (input.IsVpnControlEnabled == true)
                AddDistinct(orderedPermissions, permissions, BindVpnService);
            if (input.IsAssistantScreenContentEnabled == true)
                AddDistinct(orderedPermissions, permissions, ReadAssistStructureScreenContent);
            if (input.HasManageExternalStorageAccess == true)
                AddDistinct(orderedPermissions, permissions, ManageExternalStorage);
            if (input.CanRequestPackageInstalls == true)
                AddDistinct(orderedPermissions, permissions, RequestInstallPackages);
            if (input.CanScheduleExactAlarms == true)
                AddDistinct(orderedPermissions, permissions, ScheduleExactAlarm);
            if (input.IsIgnoringBatteryOptimizations == true)
                AddDistinct(orderedPermissions, permissions, IgnoreBatteryOptimizations);
        }
    }
}
