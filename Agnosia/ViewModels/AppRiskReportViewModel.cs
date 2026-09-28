using Agnosia.Models;

namespace Agnosia.ViewModels;

public sealed record AppRiskReportSectionViewModel(string Title, IReadOnlyList<string> Paragraphs);

public sealed record AppRiskReportViewModel(
    AppPermissionRiskLevel Level,
    string LevelText,
    string CheckedAtText,
    string Summary,
    IReadOnlyList<AppRiskReportSectionViewModel> Sections,
    string StatusText,
    string ProtectionText)
{
    public bool IsCritical => Level == AppPermissionRiskLevel.Critical && HasLevel;
    public bool IsLow => Level == AppPermissionRiskLevel.Safe;
    public bool HasLevel => LevelText.Length > 0;
    public bool HasCheckedAt => CheckedAtText.Length > 0;
    public bool HasDetailsHeader => HasLevel || HasCheckedAt;
    public bool HasSummary => Summary.Length > 0;
    public bool HasStatus => StatusText.Length > 0;
    public bool HasProtection => ProtectionText.Length > 0;

    // Provenance is deliberately not exposed through UI bindings.
    internal IReadOnlyList<AppPermissionRiskFinding> SourceFindings { get; init; } = [];
    internal IReadOnlyList<string> MeaningIds { get; init; } = [];
}
