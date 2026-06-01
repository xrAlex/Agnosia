using System.Text.Json.Serialization;
using Agnosia.Android.Services;
using Agnosia.Android.Shortcuts;

namespace Agnosia.Android.Serialization;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HiddenAppShortcutMetadata))]
[JsonSerializable(typeof(HiddenAppSessionState))]
internal sealed partial class AndroidJsonContext : JsonSerializerContext;
