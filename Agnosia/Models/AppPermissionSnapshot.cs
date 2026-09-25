namespace Agnosia.Models;

public enum AppPermissionKind { Runtime, Manifest, Special, Unknown }

public enum AppPermissionState { Unknown, NotGranted, Granted, PolicyDenied, PolicyGranted }

public sealed record AppPermissionSnapshot(
    string Name,
    string Label,
    string? Description,
    AppPermissionKind Kind,
    AppPermissionState State,
    bool CanChangePolicy,
    string? RestrictionReason,
    bool CanRevokeGrant = false);
