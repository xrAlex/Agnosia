using Agnosia.Models;

namespace Agnosia.Platform;

public interface IPermissionPlatformService
{
    Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> RequestPermissionAsync(PermissionKind permission, CancellationToken cancellationToken = default);
}
