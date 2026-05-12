using Agnosia.Models;

namespace Agnosia.Platform;

public interface IOnboardingPlatformService
{
    Task<bool> LoadOnboardingCompletedAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> CompleteOnboardingAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> OpenWorkProfileSettingsAsync(CancellationToken cancellationToken = default);
}
