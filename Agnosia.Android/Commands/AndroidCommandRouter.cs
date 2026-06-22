namespace Agnosia.Android.Commands;

internal static class AndroidCommandRouter
{
    public static AndroidCommandRoute GetRoute(AndroidCommandEnvelope envelope)
    {
        if (envelope.Interactivity == AndroidCommandInteractivity.Interactive)
            return new AndroidCommandRoute([AndroidCommandTransportKind.Activity]);

        return envelope.TargetProfile switch
        {
            AndroidCommandTargetProfile.Personal => new AndroidCommandRoute(
                [AndroidCommandTransportKind.DirectLocal]),
            AndroidCommandTargetProfile.Work => new AndroidCommandRoute(
                [AndroidCommandTransportKind.SilentWorkProfile, AndroidCommandTransportKind.Activity]),
            _ => new AndroidCommandRoute([AndroidCommandTransportKind.Activity])
        };
    }
}

internal sealed record AndroidCommandRoute(
    IReadOnlyList<AndroidCommandTransportKind> Transports);
