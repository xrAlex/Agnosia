namespace Agnosia.Models;

public sealed record AppSnapshot(
    string PackageName,
    string Label,
    string? SourceDirectory,
    IReadOnlyList<string> SplitApks,
    ProfileKind Profile,
    bool IsSystem,
    bool IsHidden,
    bool CanLaunch,
    bool IsInstalled,
    bool InteractionAllowed,
    byte[]? IconPng = null);
