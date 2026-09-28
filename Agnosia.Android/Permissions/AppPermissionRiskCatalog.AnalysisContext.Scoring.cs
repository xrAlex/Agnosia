namespace Agnosia.Android.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private sealed partial class AnalysisContext
    {
        public bool HasForegroundServiceType(string type) => _foregroundServiceTypes.Contains(type);

        public bool HasExfiltrationChannel => GetDeclaredExfiltrationPermissions().Count > 0;

        public int GetExfiltrationScore(IReadOnlyList<string>? relevantPermissions = null)
        {
            var score = 0;
            foreach (var channel in GetDeclaredExfiltrationPermissions()
                         .Where(channel => relevantPermissions is null || relevantPermissions.Contains(channel)))
                score = Math.Max(score, channel is Internet or AccessLocalNetwork ? 2 : 1);
            return score;
        }

        public int GetPersistenceScore(IReadOnlyList<string> permissions, string? foregroundServiceType)
        {
            var score = 0;
            if (ContainsPermission(permissions, BootCompleted)) score += 2;
            if ((ContainsPermission(permissions, ScheduleExactAlarm) && HasEffectivePermission(ScheduleExactAlarm))
                || (ContainsPermission(permissions, UseExactAlarm) && HasEffectivePermission(UseExactAlarm)))
                score += 1;
            if (ContainsPermission(permissions, ForegroundService) ||
                foregroundServiceType is not null && HasForegroundServiceType(foregroundServiceType)) score += 1;
            return score;
        }

        public int GetStealthScore(IReadOnlyList<string> permissions) =>
            ContainsPermission(permissions, IgnoreBatteryOptimizations)
            && HasEffectivePermission(IgnoreBatteryOptimizations) ? 2 : 0;

        public IReadOnlyList<string> GetDeclaredExfiltrationPermissions() =>
            _exfiltrationPermissions ??= BuildExfiltrationPermissions();

        private IReadOnlyList<string> BuildExfiltrationPermissions()
        {
            var permissions = new List<string>();
            if (HasEffectivePermission(Internet)) permissions.Add(Internet);
            if (DeviceSdkVersion >= Android17Api && HasEffectivePermission(AccessLocalNetwork)) permissions.Add(AccessLocalNetwork);
            if (HasEffectivePermission(BluetoothConnect)) permissions.Add(BluetoothConnect);
            if (HasEffectivePermission(Nfc)) permissions.Add(Nfc);
            if (HasEffectivePermission(SendSms)) permissions.Add(SendSms);
            if (DeviceSdkVersion <= Android12LApi && TargetSdkVersion is > 0 and <= LegacyExternalStorageMaxTargetSdk
                && HasEffectivePermission(WriteExternalStorage)) permissions.Add(WriteExternalStorage);
            if (HasEffectivePermission(ManageExternalStorage)) permissions.Add(ManageExternalStorage);
            return permissions;
        }

        private static bool ContainsPermission(IReadOnlyList<string> permissions, string expected)
        {
            foreach (var permission in permissions)
                if (string.Equals(permission, expected, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
