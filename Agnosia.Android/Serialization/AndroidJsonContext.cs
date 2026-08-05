using System.Text.Json.Serialization;

namespace Agnosia.Android.Serialization;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HiddenAppShortcutMetadata))]
internal sealed partial class AndroidJsonContext : JsonSerializerContext;
