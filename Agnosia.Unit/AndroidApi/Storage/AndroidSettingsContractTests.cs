using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Storage;

public sealed class AndroidSettingsContractTests
{
    public static IEnumerable<object?[]> MissingOrInvalidSettingsValues =>
    [
        [null],
        [""],
        ["   "],
        ["unknown"]
    ];

    // Проверяет case-insensitive чтение сохраненного theme enum.
    [Theory]
    [InlineData("Agnosia", AppThemeKind.Agnosia)]
    [InlineData("dark", AppThemeKind.Dark)]
    [InlineData("LIGHT", AppThemeKind.Light)]
    public void ParseAppTheme_accepts_saved_theme_names_case_insensitively(
        string savedValue,
        AppThemeKind expected)
    {
        var result = AndroidSettingsContract.ParseAppTheme(savedValue);

        Assert.Equal(expected, result);
    }

    // Проверяет fallback темы при пустом или неизвестном persisted value.
    [Theory]
    [MemberData(nameof(MissingOrInvalidSettingsValues))]
    public void ParseAppTheme_falls_back_to_agnosia_for_missing_or_invalid_value(string? savedValue)
    {
        var result = AndroidSettingsContract.ParseAppTheme(savedValue);

        Assert.Equal(AppThemeKind.Agnosia, result);
    }

    // Проверяет case-insensitive чтение сохраненного VPN automation client enum.
    [Theory]
    [InlineData("FlClash", VpnAutomationClientKind.FlClash)]
    [InlineData("tunguska", VpnAutomationClientKind.Tunguska)]
    [InlineData("NEKOBOXPLUS", VpnAutomationClientKind.NekoBoxPlus)]
    public void ParseVpnAfterWorkFreezeClient_accepts_saved_client_names_case_insensitively(
        string savedValue,
        VpnAutomationClientKind expected)
    {
        var result = AndroidSettingsContract.ParseVpnAfterWorkFreezeClient(savedValue);

        Assert.Equal(expected, result);
    }

    // Проверяет fallback VPN client при пустом или неизвестном persisted value.
    [Theory]
    [MemberData(nameof(MissingOrInvalidSettingsValues))]
    public void ParseVpnAfterWorkFreezeClient_falls_back_to_flclash_for_missing_or_invalid_value(
        string? savedValue)
    {
        var result = AndroidSettingsContract.ParseVpnAfterWorkFreezeClient(savedValue);

        Assert.Equal(VpnAutomationClientKind.FlClash, result);
    }

    // Проверяет normalization токена Tunguska перед записью в storage.
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(" token ", "token")]
    public void NormalizeTunguskaAutomationToken_trims_value_and_never_returns_null(
        string? value,
        string expected)
    {
        var result = AndroidSettingsContract.NormalizeTunguskaAutomationToken(value);

        Assert.Equal(expected, result);
    }
}
