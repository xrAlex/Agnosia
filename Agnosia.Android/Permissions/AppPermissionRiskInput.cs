namespace Agnosia.Android.Permissions;

public sealed record AppPermissionRiskInput(
    IEnumerable<string>? RequestedPermissions,
    int DeviceSdkVersion = 31,
    int TargetSdkVersion = 0,
    IEnumerable<string>? ForegroundServiceTypes = null,
    IEnumerable<string>? ServicePermissions = null,
    IEnumerable<string>? GrantedPermissions = null,
    IEnumerable<string>? DeniedPermissions = null,
    bool IsAccessibilityServiceEnabled = false,
    bool IsNotificationListenerEnabled = false,
    bool CanDrawOverlays = false,
    bool HasUsageStatsAccess = false,
    bool? IsCameraAppOpAllowed = null,
    bool? IsMicrophoneAppOpAllowed = null,
    bool? IsFineLocationAppOpAllowed = null,
    bool? IsCoarseLocationAppOpAllowed = null,
    IEnumerable<string>? ObservedSignals = null,
    bool IsVpnControlEnabled = false,
    bool IsAssistantScreenContentEnabled = false,
    bool IsMediaProjectionActive = false);
