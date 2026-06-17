namespace Agnosia.Models;

public sealed record PermissionSnapshot
{
    public PermissionKind Kind { get; }
    public string Title { get; }
    public string ProfileLabel { get; }
    public string Description { get; }
    public bool IsGranted { get; }
    public bool CanRequest { get; }
    public string GrantedLabel { get; }
    public string RequestLabel { get; }

    internal PermissionSnapshot(
        PermissionKind kind,
        string title,
        string profileLabel,
        string description,
        bool isGranted,
        bool canRequest,
        string grantedLabel,
        string requestLabel)
    {
        Kind = kind;
        Title = title;
        ProfileLabel = profileLabel;
        Description = description;
        IsGranted = isGranted;
        CanRequest = canRequest;
        GrantedLabel = grantedLabel;
        RequestLabel = requestLabel;
    }
}
