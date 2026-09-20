namespace Agnosia.Android.Vpn;

internal sealed record FlClashEndpoint(string PackageName, string ActivityClassName, string StartAction)
{
    private static readonly FlClashEndpoint[] Candidates =
    [
        new("com.follow.clash", "com.follow.clash.TempActivity", "com.follow.clash.action.START"),
        new("com.follow.clashx", "com.follow.clashx.TempActivity", "com.follow.clashx.action.START")
    ];

    public static FlClashEndpoint? Select(
        Func<FlClashEndpoint, bool> canStart,
        Func<string, bool>? isInstalled = null)
    {
        return Candidates.FirstOrDefault(canStart)
               ?? (isInstalled is null ? null : Candidates.FirstOrDefault(candidate => isInstalled(candidate.PackageName)));
    }
}
