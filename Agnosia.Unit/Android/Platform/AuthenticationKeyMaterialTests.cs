using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.Android.Platform;

public sealed class AuthenticationKeyMaterialTests
{
    [Fact]
    public void Create_returns_distinct_32_byte_hex_keys()
    {
        var first = AuthenticationKeyMaterial.Create();
        var second = AuthenticationKeyMaterial.Create();

        Assert.Equal(32, Convert.FromHexString(first).Length);
        Assert.Equal(32, Convert.FromHexString(second).Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void IsValid_accepts_exactly_32_bytes_of_hex_key_material()
    {
        Assert.True(AuthenticationKeyMaterial.IsValid(new string('A', 64)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("00")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAG")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void IsValid_rejects_missing_malformed_or_wrong_length_key_material(string? value)
    {
        Assert.False(AuthenticationKeyMaterial.IsValid(value));
    }
}
