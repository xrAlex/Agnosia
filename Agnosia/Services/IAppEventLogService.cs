using Agnosia.Models;

namespace Agnosia.Services;

public interface IAppEventLogService
{
    string Summary { get; }

    string Output { get; }

    IReadOnlyList<string> Lines { get; }

    bool ImportPlatformLogs(IEnumerable<AppLogEntry> logs);

    void Clear();
}