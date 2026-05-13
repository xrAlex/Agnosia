namespace Agnosia.Models;

public sealed record DashboardAppInventorySnapshot(
    IReadOnlyList<AppSnapshot> PersonalApps,
    IReadOnlyList<AppSnapshot> WorkApps)
{
    public static DashboardAppInventorySnapshot Empty { get; } = new([], []);
}
