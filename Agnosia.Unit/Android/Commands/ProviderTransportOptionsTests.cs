using Agnosia.Android.Commands;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ProviderTransportOptionsTests
{
    [Fact]
    public void Auto_transport_enables_provider_without_build_flag()
    {
        Assert.True(ProviderTransportOptions.Enabled);
    }

    [Theory]
    [InlineData(CommandTransportPreference.Provider, true)]
    [InlineData(CommandTransportPreference.Activity, false)]
    [InlineData(CommandTransportPreference.Auto, true)]
    public void SelectedTransportControlsProviderAccess(
        CommandTransportPreference preference, bool expected)
    {
        Assert.Equal(expected, ProviderTransportOptions.IsEnabled(preference));
    }
}
