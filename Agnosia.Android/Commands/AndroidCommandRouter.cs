using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal static class AndroidCommandRouter
{
    public static AndroidCommandRoute GetRoute(AndroidCommandEnvelope envelope, bool providerEnabled = false,
        CommandTransportPreference preference = CommandTransportPreference.Auto)
    {
        if (envelope.TargetProfile == AndroidCommandTargetProfile.Personal)
            return new AndroidCommandRoute([AndroidCommandTransportKind.DirectLocal]);

        if (preference == CommandTransportPreference.Activity)
            return new AndroidCommandRoute([AndroidCommandTransportKind.Activity]);

        if (preference == CommandTransportPreference.Provider)
            return new AndroidCommandRoute([AndroidCommandTransportKind.Provider]);

        if (providerEnabled && envelope.Interactivity == AndroidCommandInteractivity.NonInteractive
            && envelope.TargetProfile == AndroidCommandTargetProfile.Work
            && ProviderCommandPolicy.Supports(envelope.Kind))
            return new AndroidCommandRoute([AndroidCommandTransportKind.Provider, AndroidCommandTransportKind.Activity]);

        return new AndroidCommandRoute([AndroidCommandTransportKind.Activity]);
    }
}

internal sealed record AndroidCommandRoute(
    IReadOnlyList<AndroidCommandTransportKind> Transports);
