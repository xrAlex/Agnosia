using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal static class ProviderTransportOptions
{
    private static int _currentPreference = -1;

    public static CommandTransportPreference CurrentPreference
    {
        get
        {
            var selected = Volatile.Read(ref _currentPreference);
            if (selected >= 0) return (CommandTransportPreference)selected;
#if AGNOSIA_ANDROID
            return AndroidSettingsStore.LoadCommandTransportPreference(
                ServiceRegistry.GetRequiredService<LocalStorageManager>());
#else
            return CommandTransportPreference.Auto;
#endif
        }
    }

    public static void SetCurrentPreference(CommandTransportPreference preference) =>
        Volatile.Write(ref _currentPreference, (int)preference);

    public static bool Enabled
    {
        get => IsEnabled(CurrentPreference);
    }

    public static bool IsEnabled(CommandTransportPreference preference) =>
        preference is CommandTransportPreference.Auto or CommandTransportPreference.Provider;
}
