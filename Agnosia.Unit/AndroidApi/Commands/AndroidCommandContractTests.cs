using Agnosia.Android.Api.Commands;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Commands;

public sealed class AndroidCommandContractTests
{
    // Проверяет, что строковые ключи command contract заполнены и не конфликтуют.
    [Fact]
    public void Contract_values_are_non_empty_and_unique()
    {
        var values = StringConstantContract.ValuesOf(typeof(AndroidCommandContract));

        StringConstantContract.AssertNonEmptyUniqueValues(values);
    }

    // Проверяет стабильные literal-значения операций package installer.
    [Fact]
    public void Package_installer_operation_values_are_stable()
    {
        Assert.Equal("install", AndroidCommandContract.PackageInstallerOperationInstall);
        Assert.Equal("uninstall", AndroidCommandContract.PackageInstallerOperationUninstall);
    }
}
