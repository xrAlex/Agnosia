using Agnosia.Android.Activities;
using Xunit;

namespace Agnosia.Unit.Android.Activities;

public sealed class NewPackageInstallRecoveryTests
{
    [Fact]
    public async Task Missing_callback_is_recovered_when_new_package_appears()
    {
        var installed = false;
        var result = await NewPackageInstallRecovery.WaitAsync(false, () => installed,
            _ => { installed = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Existing_package_does_not_confirm_a_canceled_reinstall()
    {
        var result = await NewPackageInstallRecovery.WaitAsync(true, () => true,
            _ => throw new InvalidOperationException("Existing packages must await installer status."),
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Missing_package_waits_until_cancellation_instead_of_reporting_success()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewPackageInstallRecovery.WaitAsync(false, () => false,
                _ => { cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
    }

    [Fact]
    public async Task Canceled_operation_cannot_be_recovered_even_if_package_is_now_installed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewPackageInstallRecovery.WaitAsync(false, () => true,
                _ => Task.CompletedTask, cancellation.Token));
    }
}
