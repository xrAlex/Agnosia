namespace Agnosia.Models;

public sealed record AgnosiaModuleRequirement
{
    public string Title { get; }
    public string Description { get; }
    public bool IsSatisfied { get; }
    public PermissionKind? PermissionKind { get; }
    public string ActionLabel { get; }

    internal AgnosiaModuleRequirement(
        string title,
        string description,
        bool isSatisfied,
        PermissionKind? permissionKind = null,
        string actionLabel = "")
    {
        Title = title;
        Description = description;
        IsSatisfied = isSatisfied;
        PermissionKind = permissionKind;
        ActionLabel = actionLabel;
    }
}
