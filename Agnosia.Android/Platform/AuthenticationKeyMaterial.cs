using System.Security.Cryptography;

namespace Agnosia.Android.Platform;

internal static class AuthenticationKeyMaterial
{
    private const int KeyByteLength = 32;

    public static string Create()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyByteLength));
    }

    public static bool IsValid(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        try
        {
            return Convert.FromHexString(key).Length == KeyByteLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
