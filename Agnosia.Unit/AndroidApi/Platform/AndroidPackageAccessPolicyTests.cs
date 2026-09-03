using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Platform;

public sealed class AndroidPackageAccessPolicyTests
{
    private const string RequiredCrossProfilePackage = "ru.sberbankmobile";

    // Проверяет hard-coded policy package, которому нужен cross-profile interaction.
    [Fact]
    public void RequiresCrossProfileInteraction_returns_true_for_required_policy_package()
    {
        var result = AndroidPackageAccessPolicy.RequiresCrossProfileInteraction(RequiredCrossProfilePackage);

        Assert.True(result);
    }

    // Проверяет strict matching и отказ для пустых или неизвестных package names.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("com.example.app")]
    [InlineData("RU.SBERBANKMOBILE")]
    public void RequiresCrossProfileInteraction_returns_false_for_unknown_or_invalid_package(string? packageName)
    {
        var result = AndroidPackageAccessPolicy.RequiresCrossProfileInteraction(packageName);

        Assert.False(result);
    }

    // Проверяет, что required policy package добавляется даже без входного списка.
    [Fact]
    public void ApplyRequiredCrossProfilePackages_adds_required_package_for_null_input()
    {
        var result = AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages(null);

        Assert.Equal([RequiredCrossProfilePackage], result);
    }

    // Проверяет фильтрацию blank значений, dedupe и сортировку итогового списка.
    [Fact]
    public void ApplyRequiredCrossProfilePackages_filters_blank_values_deduplicates_and_sorts()
    {
        var result = AndroidPackageAccessPolicy.ApplyRequiredCrossProfilePackages([
            "z.example",
            "",
            RequiredCrossProfilePackage,
            "   ",
            "a.example",
            "z.example"
        ]);

        Assert.Equal(
            [
                "a.example",
                RequiredCrossProfilePackage,
                "z.example"
            ],
            result);
    }
}
