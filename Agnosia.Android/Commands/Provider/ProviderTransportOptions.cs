namespace Agnosia.Android.Commands;

internal static class ProviderTransportOptions
{
    // Opt in with -p:AgnosiaCommandProvider=true until cross-profile grants are verified on devices.
    public static bool Enabled
    {
        get
        {
#if AGNOSIA_COMMAND_PROVIDER
            return true;
#else
            return false;
#endif
        }
    }
}
