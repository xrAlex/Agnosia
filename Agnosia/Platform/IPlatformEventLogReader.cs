using Agnosia.Models;

namespace Agnosia.Platform;

public interface IPlatformEventLogReader
{
    Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken = default);
}
