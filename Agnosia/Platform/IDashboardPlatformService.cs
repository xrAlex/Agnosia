using Agnosia.Models;

namespace Agnosia.Platform;

public interface IDashboardPlatformService
{
    Task<DashboardSnapshot> LoadDashboardProfileAsync(CancellationToken cancellationToken = default);

    Task<DashboardAppInventorySnapshot> LoadAppInventoryAsync(
        DashboardSnapshot profileSnapshot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<AppItemKey, byte[]?>> LoadAppIconsAsync(
        IReadOnlyList<AppSnapshot> apps,
        CancellationToken cancellationToken = default);
}
