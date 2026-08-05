using System.Text.Json.Serialization;

namespace Agnosia.Android.Vpn;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(VpnRestoreOwnershipState))]
[JsonSerializable(typeof(VpnRestoreOwner))]
internal sealed partial class VpnRestoreOwnershipJsonContext : JsonSerializerContext;
