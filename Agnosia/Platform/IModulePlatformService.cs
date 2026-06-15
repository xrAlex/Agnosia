using Agnosia.Models;

namespace Agnosia.Platform;

public interface IModulePlatformService
{
    Task<IReadOnlyList<AgnosiaModuleSnapshot>> LoadModulesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgnosiaModuleSnapshot>> LoadModulesAsync(
        IReadOnlyList<PermissionSnapshot> permissions,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetModuleEnabledAsync(
        AgnosiaModuleKind module,
        bool enabled,
        CancellationToken cancellationToken = default);
}
