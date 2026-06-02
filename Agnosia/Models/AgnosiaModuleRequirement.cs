namespace Agnosia.Models;

public sealed record AgnosiaModuleRequirement(
    string Title,
    string Description,
    bool IsSatisfied,
    PermissionKind? PermissionKind = null,
    string ActionLabel = "");
