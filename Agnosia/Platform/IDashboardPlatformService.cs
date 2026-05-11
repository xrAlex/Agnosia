using Agnosia.Models;

namespace Agnosia.Platform;

public interface IDashboardPlatformService
{
    Task<DashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken = default);
}
