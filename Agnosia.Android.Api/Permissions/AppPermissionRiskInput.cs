namespace Agnosia.Android.Api.Permissions;

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
    bool? IsCoarseLocationAppOpAllowed = null);
