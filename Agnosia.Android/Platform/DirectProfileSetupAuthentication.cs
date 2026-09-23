using System.Security.Cryptography;

namespace Agnosia.Android.Platform;

internal static class DirectProfileSetupAuthentication
{
    public static bool CanAccept(string? existingKey, string? incomingKey, bool isManagedProfile,
        bool isProfileOwner, int actualUserId, int expectedUserId)
    {
        if (!isManagedProfile || !isProfileOwner || actualUserId <= 0 || actualUserId != expectedUserId
            || !AuthenticationKeyMaterial.IsValid(incomingKey)) return false;
        if (existingKey is null) return true;
        return AuthenticationKeyMaterial.IsValid(existingKey)
               && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existingKey), Convert.FromHexString(incomingKey!));
    }
}
