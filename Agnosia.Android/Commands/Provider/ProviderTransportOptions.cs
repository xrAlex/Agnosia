using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal static class ProviderTransportOptions
{
    public static bool Enabled
    {
        get
        {
#if AGNOSIA_ANDROID
            var preference = AndroidSettingsStore.LoadCommandTransportPreference(
                ServiceRegistry.GetRequiredService<LocalStorageManager>());
#else
            var preference = CommandTransportPreference.Auto;
#endif
            return IsEnabled(preference);
        }
    }

    public static bool IsEnabled(CommandTransportPreference preference) =>
        preference is CommandTransportPreference.Auto or CommandTransportPreference.Provider;
}
