namespace Agnosia.Models;

public sealed record PermissionSnapshot(
    PermissionKind Kind,
    string Title,
    string ProfileLabel,
    string Description,
    bool IsGranted,
    bool CanRequest,
    string GrantedLabel,
    string RequestLabel);
