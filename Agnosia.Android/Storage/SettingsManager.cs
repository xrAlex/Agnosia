using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Storage;

public sealed class SettingsManager
{
    private static SettingsManager? _instance;

    private readonly Context _context;

    private SettingsManager(Context context)
    {
        _context = context;
    }

    public static void Initialize(Context context)
    {
        _instance = new SettingsManager(context);
    }

    public static SettingsManager Instance =>
        _instance ?? throw new InvalidOperationException("SettingsManager has not been initialized yet.");

    public Task<OperationResult> SyncBooleanSettingAsync(
        string name,
        bool value,
        CancellationToken cancellationToken = default)
    {
        return AndroidProfileCommandGateway.SynchronizeBooleanToWorkProfileAsync(_context, name, value,
            cancellationToken);
    }
}
