using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Storage;

public sealed class SettingsManager
{
    private readonly Context _context;

    public SettingsManager(Context context)
    {
        _context = context;
    }

    public Task<OperationResult> SyncBooleanSettingAsync(
        string name,
        bool value,
        CancellationToken cancellationToken = default)
    {
        return AndroidProfileCommandGateway.SynchronizeBooleanToWorkProfileAsync(_context, name, value,
            cancellationToken);
    }
}
