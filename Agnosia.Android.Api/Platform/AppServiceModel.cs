using Agnosia.Models;

namespace Agnosia.Android.Api.Platform;

public sealed class AppServiceModel
{
    public required string PackageName { get; init; }

    public required string Label { get; init; }

    public string? SourceDirectory { get; init; }

    public string[] SplitApks { get; init; } = [];

    public byte[]? IconPng { get; init; }

    public bool IsSystem { get; init; }

    public bool IsHidden { get; init; }

    public bool CanLaunch { get; init; }

    public bool IsInstalled { get; init; }

    public AppPermissionRiskLevel PermissionRiskLevel { get; init; } = AppPermissionRiskLevel.Safe;

    public string[] RiskyPermissions { get; init; } = [];
}
