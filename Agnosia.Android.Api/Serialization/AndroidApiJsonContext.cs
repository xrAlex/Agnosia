using System.Text.Json.Serialization;
using Agnosia.Android.Api.Platform;
using Agnosia.Models;

namespace Agnosia.Android.Api.Serialization;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<AppLogEntry>))]
[JsonSerializable(typeof(List<AppServiceModel>))]
public sealed partial class AndroidApiJsonContext : JsonSerializerContext;
