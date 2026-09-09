using Agnosia.Android.Vpn;
using Xunit;

namespace Agnosia.Unit.Android.Vpn;

public sealed class FlClashEndpointTests
{
    [Theory]
    [InlineData(true, false, "com.follow.clash")]
    [InlineData(false, true, "com.follow.clashx")]
    [InlineData(true, true, "com.follow.clash")]
    [InlineData(false, false, null)]
    public void Selects_complete_available_endpoint_in_priority_order(bool clash, bool clashx, string? expected)
    {
        var selected = FlClashEndpoint.Select(endpoint => endpoint.PackageName switch
        {
            "com.follow.clash" => clash && endpoint.ActivityClassName == "com.follow.clash.TempActivity"
                && endpoint.StartAction == "com.follow.clash.action.START",
            "com.follow.clashx" => clashx && endpoint.ActivityClassName == "com.follow.clashx.TempActivity"
                && endpoint.StartAction == "com.follow.clashx.action.START",
            _ => false
        });
        Assert.Equal(expected, selected?.PackageName);
    }
}
