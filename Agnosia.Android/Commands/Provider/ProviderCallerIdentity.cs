namespace Agnosia.Android.Commands;

internal static class ProviderCallerIdentity
{
    // Android UserHandle.getAppId(uid) is uid % PER_USER_RANGE. Both APIs are hidden
    // from the app SDK, while the UID layout is stable across supported Android versions.
    private const int PerUserRange = 100_000;

    public static bool IsSameApp(int callerUid, int ownUid) => callerUid >= 0 && ownUid >= 0
        && callerUid % PerUserRange == ownUid % PerUserRange;
}
