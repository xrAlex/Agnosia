using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.OS;
using Process = Android.OS.Process;

namespace Agnosia.Android.Commands;

internal static class ProviderProfileIdentity
{
    // UserHandle.hashCode() returns the Android user identifier (AOSP UserHandle).
    public static int UserId(UserHandle? user) => user?.GetHashCode()
        ?? throw new InvalidOperationException("Android user handle is unavailable.");
    public static int CurrentUserId => UserId(Process.MyUserHandle());

    public static string Generation(Context context, string key)
    {
        var serial = AndroidSystemApi.GetUserManager(context)?.GetSerialNumberForUser(Process.MyUserHandle()) ?? -1;
        if (serial < 0) throw new InvalidOperationException("Profile serial number is unavailable.");
        return Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(key),
            Encoding.UTF8.GetBytes($"AGNOSIA_PROVIDER_GENERATION_1:{CurrentUserId}:{serial}")));
    }

    public static bool IsSibling(Context context, int userId) => userId != CurrentUserId
        && AndroidSystemApi.GetUserManager(context)?.UserProfiles?.Any(user => UserId(user) == userId) == true;
}
