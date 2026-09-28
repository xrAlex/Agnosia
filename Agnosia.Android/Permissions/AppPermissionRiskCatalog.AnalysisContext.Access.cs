namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private sealed partial class AnalysisContext
    {
        public bool HasPermission(string permission) => _permissions.Contains(permission);

        public bool HasPermissionPrefix(string prefix) =>
            _orderedPermissions.Any(permission => permission.StartsWith(prefix, StringComparison.Ordinal));

        public IEnumerable<string> GetPermissionsByPrefix(string prefix) =>
            _orderedPermissions.Where(permission => permission.StartsWith(prefix, StringComparison.Ordinal));

        public bool HasEffectivePermission(string permission)
        {
            if (!HasPermission(permission)) return false;

            if (TryGetSpecialAccessState(permission, out var specialAccessState))
                return specialAccessState == true;

            if (IsBlockedByAppOp(permission) || GetGrantStatus(permission) == PermissionGrantStatus.Denied) return false;

            if (permission == AccessBackgroundLocation)
                return HasGrantedPermission(permission) &&
                       (HasEffectivePermission(AccessFineLocation) && !IsForegroundOnly(AccessFineLocation)
                        || HasEffectivePermission(AccessCoarseLocation) && !IsForegroundOnly(AccessCoarseLocation));

            if (IsRuntimeSensitivePermission(permission))
                return GetGrantStatus(permission) == PermissionGrantStatus.Granted;

            return true;
        }

        public bool HasEffectivePermissionPrefix(string prefix) =>
            _orderedPermissions.Any(permission => permission.StartsWith(prefix, StringComparison.Ordinal)
                                                  && HasEffectivePermission(permission));

        public bool HasGrantedPermission(string permission) => _grantedPermissions.Contains(permission);

        public bool HasDeniedPermission(string permission) => _deniedPermissions.Contains(permission);

        public PermissionGrantStatus GetGrantStatus(string permission)
        {
            if (HasDeniedPermission(permission)) return PermissionGrantStatus.Denied;
            if (HasGrantedPermission(permission)) return PermissionGrantStatus.Granted;
            return PermissionGrantStatus.Unknown;
        }

        public bool IsRuntimeSensitivePermission(string permission)
        {
            return permission.StartsWith("android.permission.health.", StringComparison.Ordinal)
                   || permission is ReadCalendar or WriteCalendar or WriteContacts or CallPhone
                       or ReceiveMms or ReceiveWapPush or BodySensors or BodySensorsBackground
                       or ActivityRecognition or UwbRanging or BluetoothAdvertise or GetAccounts
                       or AccessBackgroundLocation
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

        public bool HasEnabledControlSurface(string permission) =>
            TryGetSpecialAccessState(permission, out var isEnabled) && isEnabled == true;

        private bool TryGetSpecialAccessState(string permission, out bool? isEnabled)
        {
            isEnabled = permission switch
            {
                BindAccessibilityService => IsAccessibilityServiceEnabled,
                BindInputMethod => IsInputMethodEnabled,
                BindAutofillService => IsAutofillServiceEnabled,
                BindDeviceAdmin => IsDeviceAdminEnabled,
                BindNotificationListenerService => IsNotificationListenerEnabled,
                BindVpnService => IsVpnControlEnabled,
                ReadAssistStructureScreenContent => IsAssistantScreenContentEnabled,
                SystemAlertWindow => CanDrawOverlays,
                PackageUsageStats => HasUsageStatsAccess,
                ManageExternalStorage => HasManageExternalStorageAccess,
                RequestInstallPackages => CanRequestPackageInstalls,
                ScheduleExactAlarm or UseExactAlarm => CanScheduleExactAlarms,
                IgnoreBatteryOptimizations => IsIgnoringBatteryOptimizations,
                _ => false
            };

            return permission is BindAccessibilityService
                or BindInputMethod or BindAutofillService or BindDeviceAdmin
                or BindNotificationListenerService
                or BindVpnService
                or ReadAssistStructureScreenContent
                or SystemAlertWindow
                or PackageUsageStats
                or ManageExternalStorage
                or RequestInstallPackages
                or ScheduleExactAlarm
                or UseExactAlarm
                or IgnoreBatteryOptimizations;
        }

        public bool IsBlockedByAppOp(string permission)
        {
            if (BlockedAppOpPermissions.Contains(permission)) return true;
            return permission switch
            {
                Camera => IsCameraAppOpAllowed == false,
                RecordAudio => IsMicrophoneAppOpAllowed == false,
                AccessFineLocation => IsFineLocationAppOpAllowed == false,
                AccessCoarseLocation => IsCoarseLocationAppOpAllowed == false,
                _ => false
            };
        }
    }
}
