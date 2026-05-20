namespace Agnosia.Models;

public sealed record AppPermissionRiskAnalysis(
    AppPermissionRiskLevel Level,
    IReadOnlyList<string> RiskyPermissions)
{
    public static AppPermissionRiskAnalysis Safe { get; } = new(AppPermissionRiskLevel.Safe, []);
}
