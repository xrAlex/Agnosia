using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;

namespace Agnosia.Android.Api.Serialization;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AndroidAppLaunchResult))]
public sealed partial class AndroidAppLaunchJsonContext : JsonSerializerContext;
