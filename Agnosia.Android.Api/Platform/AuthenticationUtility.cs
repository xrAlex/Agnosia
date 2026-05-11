using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Java.Lang;
using Boolean = Java.Lang.Boolean;
using Math = System.Math;
using Object = Java.Lang.Object;
using String = Java.Lang.String;
using StringBuilder = System.Text.StringBuilder;

namespace Agnosia.Android.Api;

public static class AuthenticationUtility
{
    public const string ProvisioningAuthKeyExtra = "agnosia.provisioning.auth_key";

    private const string IntentSignaturePayloadVersion = "AGNOSIA_INTENT_SIGNATURE_1";
    private const string WorkAppFrozenCallbackPayloadVersion = "AGNOSIA_WORK_APP_FROZEN_CALLBACK_1";
    private const string ExtraAuthKey = "auth_key";
    private const string ExtraSignature = "signature";
    private const string ExtraTimestamp = "timestamp";
    private const int AuthKeyByteLength = 32;
    private static readonly TimeSpan IntentSignatureMaximumAge = TimeSpan.FromSeconds(30);

    public static void SignIntent(Intent intent)
    {
        var key = GetExistingKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No stored Agnosia authentication key is available.");
        }

        intent.RemoveExtra(ExtraAuthKey);
        intent.RemoveExtra(ExtraSignature);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        intent.PutExtra(ExtraTimestamp, timestamp);
        intent.PutExtra(ExtraSignature, SignPayload(key, CreateIntentSignaturePayload(intent, timestamp)));
    }

    public static string? GetExistingKey()
    {
        var key = LocalStorageManager.Instance.GetString(StorageKeys.AuthKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (IsValidKey(key))
        {
            return key;
        }

        LocalStorageManager.Instance.Remove(StorageKeys.AuthKey);
        return null;
    }

    public static string CreateAndStoreKey()
    {
        var key = CreateKey();
        LocalStorageManager.Instance.SetString(StorageKeys.AuthKey, key);
        return key;
    }

    public static bool TryStoreProvisioningKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !IsValidKey(key))
        {
            return false;
        }

        LocalStorageManager.Instance.SetString(StorageKeys.AuthKey, key);
        return true;
    }

    public static string SignPayload(string hexKey, string payload)
    {
        var keyBytes = Convert.FromHexString(hexKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(payloadBytes));
    }

    public static bool IsFreshTimestamp(long timestamp)
    {
        var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;
        return age >= 0 && age <= IntentSignatureMaximumAge.TotalMilliseconds;
    }

    public static bool FixedTimeEqualsHex(string? actualHex, string expectedHex)
    {
        if (string.IsNullOrWhiteSpace(actualHex))
        {
            return false;
        }

        try
        {
            var actualBytes = Convert.FromHexString(actualHex);
            var expectedBytes = Convert.FromHexString(expectedHex);
            if (actualBytes.Length != expectedBytes.Length)
            {
                var paddedActual = new byte[expectedBytes.Length];
                Array.Copy(actualBytes, paddedActual, Math.Min(actualBytes.Length, paddedActual.Length));
                CryptographicOperations.FixedTimeEquals(paddedActual, expectedBytes);
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool CheckIntent(Intent? intent)
    {
        if (intent is null)
        {
            return false;
        }

        var storage = LocalStorageManager.Instance;
        var key = storage.GetString(StorageKeys.AuthKey);
        if (string.IsNullOrWhiteSpace(key) || !IsValidKey(key))
        {
            storage.Remove(StorageKeys.AuthKey);
            return false;
        }

        var intentTimestamp = intent.GetLongExtra(ExtraTimestamp, 0);
        if (!IsFreshTimestamp(intentTimestamp))
        {
            return false;
        }

        var signature = intent.GetStringExtra(ExtraSignature);
        var expectedSignature = SignPayload(key, CreateIntentSignaturePayload(intent, intentTimestamp));
        return FixedTimeEqualsHex(signature, expectedSignature);
    }

    public static void SignWorkAppFrozenCallback(Intent intent, string packageName)
    {
        var key = GetExistingKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No stored Agnosia authentication key is available.");
        }

        intent.PutExtra(AndroidCommandContract.ExtraCallbackPackage, packageName);
        intent.PutExtra(
            AndroidCommandContract.ExtraCallbackSignature,
            SignPayload(key, CreateWorkAppFrozenCallbackPayload(packageName)));
    }

    public static bool CheckWorkAppFrozenCallback(Intent? intent)
    {
        if (intent is null
            || !string.Equals(intent.Action, AgnosiaActions.WorkAppFrozen, StringComparison.Ordinal))
        {
            return false;
        }

        var packageName = intent.GetStringExtra(AndroidCommandContract.ExtraCallbackPackage);
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return false;
        }

        var key = GetExistingKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var signature = intent.GetStringExtra(AndroidCommandContract.ExtraCallbackSignature);
        var expectedSignature = SignPayload(key, CreateWorkAppFrozenCallbackPayload(packageName));
        return FixedTimeEqualsHex(signature, expectedSignature);
    }

    public static void Reset() => LocalStorageManager.Instance.Remove(StorageKeys.AuthKey);

    private static string CreateKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(AuthKeyByteLength));

    private static string CreateIntentSignaturePayload(Intent intent, long timestamp)
    {
        var payload = new StringBuilder();
        payload.Append(IntentSignaturePayloadVersion).Append('\n');
        payload.Append(intent.Action ?? string.Empty).Append('\n');
        payload.Append(timestamp.ToString(CultureInfo.InvariantCulture)).Append('\n');

        var extras = intent.Extras;
        if (extras is null)
        {
            return payload.ToString();
        }

        var keys = extras.KeySet();
        if (keys is null)
        {
            return payload.ToString();
        }

        foreach (var key in keys
                     .Where(static key => IsSignedExtra(key))
                     .OrderBy(static key => key, StringComparer.Ordinal))
        {
            payload.Append(key).Append('=').Append(EncodeExtraValue(extras, key)).Append('\n');
        }

        return payload.ToString();
    }

    private static bool IsValidKey(string hexKey)
    {
        try
        {
            return Convert.FromHexString(hexKey).Length == AuthKeyByteLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsSignedExtra(string key) =>
        !string.Equals(key, ExtraAuthKey, StringComparison.Ordinal)
        && !string.Equals(key, ExtraSignature, StringComparison.Ordinal)
        && !string.Equals(key, ExtraTimestamp, StringComparison.Ordinal)
        && !string.Equals(key, AndroidCommandContract.ExtraParentFrozenCallback, StringComparison.Ordinal);

    private static string CreateWorkAppFrozenCallbackPayload(string packageName) =>
        WorkAppFrozenCallbackPayloadVersion + "\n" + packageName;

    private static string EncodeExtraValue(Bundle extras, string key)
    {
        var value = extras.Get(key);
        if (TryEncodeStringArray(extras, key, value, out var encodedStringArray))
        {
            return encodedStringArray;
        }

        return value switch
        {
            null => "null:",
            String javaStringValue => "string:" + EncodeString(javaStringValue.ToString() ?? string.Empty),
            Boolean booleanValue => "bool:" + booleanValue.BooleanValue().ToString(CultureInfo.InvariantCulture),
            Integer integerValue => "int:" + integerValue.IntValue().ToString(CultureInfo.InvariantCulture),
            Long longValue => "long:" + longValue.LongValue().ToString(CultureInfo.InvariantCulture),
            _ => value.GetType().FullName + ":" + EncodeString(value.ToString() ?? string.Empty)
        };
    }

    private static bool TryEncodeStringArray(Bundle extras, string key, object? value, out string encodedValue)
    {
        if (value is string[] stringValues)
        {
            encodedValue = EncodeStringArray(stringValues);
            return true;
        }

        if (value is Object javaObject
            && string.Equals(javaObject.Class?.Name, "[Ljava.lang.String;", StringComparison.Ordinal)
            && extras.GetStringArray(key) is { } javaStringValues)
        {
            encodedValue = EncodeStringArray(javaStringValues);
            return true;
        }

        encodedValue = string.Empty;
        return false;
    }

    private static string EncodeStringArray(IEnumerable<string> values) =>
        "string[]:" + string.Join(",", values.Select(EncodeString));

    private static string EncodeString(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
