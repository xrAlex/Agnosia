using Agnosia.Android.Api.Storage;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Storage;

public sealed class StorageKeysTests
{
    public static TheoryData<string, string> PersistedPreferenceKeys => new()
    {
        { nameof(StorageKeys.ShowAllApps), "show_all_apps" },
        { nameof(StorageKeys.DisableVpnBeforeWorkLaunch), "disable_vpn_before_work_launch" },
        { nameof(StorageKeys.EnableVpnAfterWorkFreeze), "enable_vpn_after_work_freeze" },
        { nameof(StorageKeys.VpnAfterWorkFreezeClient), "vpn_after_work_freeze_client" },
        { nameof(StorageKeys.TunguskaAutomationToken), "tunguska_automation_token" },
        { nameof(StorageKeys.LoggingEnabled), "logging_enabled" },
        { nameof(StorageKeys.AppTheme), "app_theme" }
    };

    // Проверяет, что storage keys заполнены и не конфликтуют между собой.
    [Fact]
    public void Storage_keys_are_non_empty_and_unique()
    {
        var keys = StringConstantContract.ValuesOf(typeof(StorageKeys));

        StringConstantContract.AssertNonEmptyUniqueValues(keys);
    }

    // Проверяет, что metadata prefix зарезервирован только как префикс.
    [Fact]
    public void Hidden_shortcut_metadata_key_is_reserved_as_prefix()
    {
        var concreteKeys = StringConstantContract.ValuesOf(typeof(StorageKeys))
            .Where(key => !string.Equals(key, StorageKeys.HiddenShortcutMetadataPrefix, StringComparison.Ordinal));

        Assert.EndsWith(":", StorageKeys.HiddenShortcutMetadataPrefix);
        Assert.DoesNotContain(concreteKeys, key =>
            key.StartsWith(StorageKeys.HiddenShortcutMetadataPrefix, StringComparison.Ordinal));
    }

    // Проверяет стабильные literal-значения persisted settings keys.
    [Theory]
    [MemberData(nameof(PersistedPreferenceKeys))]
    public void Persisted_preference_keys_keep_stable_storage_names(string constantName, string expectedValue)
    {
        var key = typeof(StorageKeys)
            .GetField(constantName)
            ?.GetRawConstantValue();

        Assert.Equal(expectedValue, key);
    }
}
