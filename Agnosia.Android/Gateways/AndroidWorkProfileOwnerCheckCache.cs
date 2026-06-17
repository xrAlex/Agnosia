namespace Agnosia.Android.Gateways;

internal static class AndroidWorkProfileOwnerCheckCache
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromSeconds(3);
    private static readonly Lock Sync = new();

    private static WorkProfileOwnerCheckResult? _cachedSuccess;
    private static DateTimeOffset _expiresAtUtc;

    public static bool TryGetSuccess(out WorkProfileOwnerCheckResult result)
    {
        lock (Sync)
        {
            if (_cachedSuccess is not null && DateTimeOffset.UtcNow <= _expiresAtUtc)
            {
                result = _cachedSuccess;
                return true;
            }

            _cachedSuccess = null;
            result = null!;
            return false;
        }
    }

    public static void StoreIfSuccess(WorkProfileOwnerCheckResult result)
    {
        if (result.Kind != WorkProfileOwnerCheckKind.AppIsProfileOwner) return;

        lock (Sync)
        {
            _cachedSuccess = result;
            _expiresAtUtc = DateTimeOffset.UtcNow + SuccessTtl;
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            _cachedSuccess = null;
            _expiresAtUtc = default;
        }
    }
}
