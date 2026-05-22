using Agnosia.Models;

namespace Agnosia.Platform;

public interface IAppCommandService
{
    Task<OperationResult> CloneAsync(AppSnapshot app, CancellationToken cancellationToken = default);

    Task<OperationResult> UninstallAsync(AppSnapshot app, CancellationToken cancellationToken = default);

    Task<OperationResult> SetFrozenAsync(AppSnapshot app, bool hidden, CancellationToken cancellationToken = default);

    Task<OperationResult> ForceFreezeAsync(AppSnapshot app, CancellationToken cancellationToken = default);

    Task<OperationResult> CreateShortcutAsync(AppSnapshot app, CancellationToken cancellationToken = default);

    Task<OperationResult> LaunchAsync(AppSnapshot app, CancellationToken cancellationToken = default);

    Task<OperationResult> SetInteractionAccessAsync(AppSnapshot app, bool enabled,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RevokeRuntimePermissionsAsync(AppSnapshot app, CancellationToken cancellationToken = default);
}
