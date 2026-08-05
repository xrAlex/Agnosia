namespace Agnosia.Android.Commands.Transports;

internal static class AndroidCommandReplyTimeoutPolicy
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinimumReplyTimeout = TimeSpan.FromSeconds(1);
    private const double EarlyFallbackTimeoutRatio = 0.75;

    public static TimeSpan GetReplyTimeout(AndroidCommandEnvelope envelope)
    {
        var timeout = NormalizeTimeout(envelope.Timeout);
        if (UsesFullReplyWindow(envelope.Kind))
            return timeout;

        var replyTimeout = TimeSpan.FromMilliseconds(timeout.TotalMilliseconds * EarlyFallbackTimeoutRatio);
        return NormalizeTimeout(replyTimeout);
    }

    private static TimeSpan NormalizeTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            return DefaultCommandTimeout;

        return timeout < MinimumReplyTimeout
            ? MinimumReplyTimeout
            : timeout;
    }

    private static bool UsesFullReplyWindow(AndroidCommandKind kind)
    {
        // QueryApps builds the work-profile inventory before returning the first page.
        // Early timeout launches the Activity fallback while the silent service is still working.
        return kind == AndroidCommandKind.QueryApps;
    }
}
