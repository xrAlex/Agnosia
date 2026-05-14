using Agnosia.Models;

namespace Agnosia.Platform;

public interface ISettingsPlatformService
{
    Task<OperationResult> SaveSettingsAsync(AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default);
}