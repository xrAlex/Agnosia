using System.Globalization;
using System.Text;

namespace Agnosia.Android.Platform;

internal static class IntentSignatureValueEncoder
{
    public static string Encode(object? value) => value switch
    {
        null => "null:",
        string text => "string:" + EncodeString(text),
        bool boolean => "bool:" + boolean.ToString(CultureInfo.InvariantCulture),
        int number => "int:" + number.ToString(CultureInfo.InvariantCulture),
        long number => "long:" + number.ToString(CultureInfo.InvariantCulture),
        string[] values => "string[]:" + string.Join(",", values.Select(EncodeString)),
        byte[] bytes => "bytes:" + Convert.ToBase64String(bytes),
        IReadOnlyDictionary<string, object?> bundle => "bundle:" + string.Join(",", bundle
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => EncodeString(pair.Key) + "=" + EncodeString(Encode(pair.Value)))),
        _ => throw new NotSupportedException($"Unsupported signed extra type: {value.GetType().FullName}.")
    };

    private static string EncodeString(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
