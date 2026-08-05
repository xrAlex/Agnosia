using System.Text.Json.Serialization;

namespace Agnosia.Android.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HiddenAppSessionStoreState))]
[JsonSerializable(typeof(HiddenAppSessionState))]
[JsonSerializable(typeof(LegacyHiddenAppSessionState))]
[JsonSerializable(typeof(LegacyHiddenAppSessionStoreStateV1))]
internal sealed partial class HiddenAppSessionStoreJsonContext : JsonSerializerContext;
