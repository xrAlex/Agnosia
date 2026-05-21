namespace Agnosia.Models;

public sealed record AppPermissionRiskInput(
    IEnumerable<string>? RequestedPermissions,
    int DeviceSdkVersion = 31,
    int TargetSdkVersion = 0,
    IEnumerable<string>? ForegroundServiceTypes = null,
    IEnumerable<string>? ServicePermissions = null);
