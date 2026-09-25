using Agnosia.Models;

namespace Agnosia.Platform;

public interface ISettingsPlatformService
{
    void SetCommandTransportPreference(CommandTransportPreference preference);

    Task<OperationResult> SaveSettingsAsync(AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default);

    Task<OperationResult> OpenDocumentsUiAsync(CancellationToken cancellationToken = default);
}
