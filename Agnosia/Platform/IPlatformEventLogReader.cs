using Agnosia.Models;

namespace Agnosia.Platform;

public interface IPlatformEventLogReader
{
    Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ClearRecentLogsAsync(CancellationToken cancellationToken = default);

    string GetDeviceInfoString();
}
