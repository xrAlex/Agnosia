using System.Text.Json.Serialization;

namespace Agnosia.Android.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HiddenAppSessionStoreState))]
[JsonSerializable(typeof(HiddenAppSessionState))]
[JsonSerializable(typeof(LegacyHiddenAppSessionState))]
internal sealed partial class HiddenAppSessionStoreJsonContext : JsonSerializerContext;
