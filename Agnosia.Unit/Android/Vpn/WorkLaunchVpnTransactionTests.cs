using Agnosia.Android.Vpn;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Vpn;

public sealed class WorkLaunchVpnTransactionTests
{
    // Ловит takeover VPN до work-profile preflight.
    [Fact]
    public async Task ExecuteAsync_stops_before_takeover_when_preflight_fails()
    {
        var calls = new List<string>();

        var result = await WorkLaunchVpnTransaction.ExecuteAsync(
            _ => Complete("preflight", OperationResult.Failure("profile unavailable")),
            _ => Complete("takeover", OperationResult.Success("vpn disabled")),
            _ => Complete("launch", OperationResult.Success("session started")),
            () => Complete("rollback", OperationResult.Success("vpn restored")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(["preflight"], calls);
        return;

        Task<OperationResult> Complete(string call, OperationResult operationResult)
        {
            calls.Add(call);
            return Task.FromResult(operationResult);
        }
    }

    // Ловит потерю rollback после quiet-mode/transfer/timeout failure downstream launch.
    [Fact]
    public async Task ExecuteAsync_rolls_back_after_launch_failure()
    {
        var calls = new List<string>();

        var result = await WorkLaunchVpnTransaction.ExecuteAsync(
            _ => Complete("preflight", OperationResult.Success(string.Empty)),
            _ => Complete("takeover", OperationResult.Success("vpn disabled")),
            _ => Complete("launch", OperationResult.Failure("transfer failed")),
            () => Complete("rollback", OperationResult.Success("vpn restored")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("transfer failed", result.Message);
        Assert.Equal(["preflight", "takeover", "launch", "rollback"], calls);
        return;

        Task<OperationResult> Complete(string call, OperationResult operationResult)
        {
            calls.Add(call);
            return Task.FromResult(operationResult);
        }
    }

    // Ловит отказ rollback, когда takeover уже мог отключить VPN, но сам вернул failure.
    [Fact]
    public async Task ExecuteAsync_rolls_back_after_takeover_failure()
    {
        var calls = new List<string>();

        var result = await WorkLaunchVpnTransaction.ExecuteAsync(
            _ => Complete("preflight", OperationResult.Success(string.Empty)),
            _ => Complete("takeover", OperationResult.Failure("disconnect failed")),
            _ => Complete("launch", OperationResult.Success("session started")),
            () => Complete("rollback", OperationResult.Success("vpn restored")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("disconnect failed", result.Message);
        Assert.Equal(["preflight", "takeover", "rollback"], calls);
        return;

        Task<OperationResult> Complete(string call, OperationResult operationResult)
        {
            calls.Add(call);
            return Task.FromResult(operationResult);
        }
    }

    // Ловит пропуск rollback, когда launch отменён после takeover.
    [Fact]
    public async Task ExecuteAsync_rolls_back_and_preserves_cancellation()
    {
        var rolledBack = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorkLaunchVpnTransaction.ExecuteAsync(
                _ => Task.FromResult(OperationResult.Success(string.Empty)),
                _ => Task.FromResult(OperationResult.Success("vpn disabled")),
                _ => Task.FromCanceled<OperationResult>(new CancellationToken(canceled: true)),
                () =>
                {
                    rolledBack = true;
                    return Task.FromResult(OperationResult.Success("vpn restored"));
                },
                CancellationToken.None));

        Assert.True(rolledBack);
    }

    // Ловит пропуск rollback при exception downstream запуска.
    [Fact]
    public async Task ExecuteAsync_rolls_back_and_preserves_launch_exception()
    {
        var rolledBack = false;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkLaunchVpnTransaction.ExecuteAsync(
                _ => Task.FromResult(OperationResult.Success(string.Empty)),
                _ => Task.FromResult(OperationResult.Success("vpn disabled")),
                _ => throw new InvalidOperationException("launch crashed"),
                () =>
                {
                    rolledBack = true;
                    return Task.FromResult(OperationResult.Success("vpn restored"));
                },
                CancellationToken.None));

        Assert.Equal("launch crashed", exception.Message);
        Assert.True(rolledBack);
    }

    // Ловит преждевременное восстановление после подтверждённого создания work-сессии.
    [Fact]
    public async Task ExecuteAsync_commits_confirmed_launch_without_rollback()
    {
        var rolledBack = false;

        var result = await WorkLaunchVpnTransaction.ExecuteAsync(
            _ => Task.FromResult(OperationResult.Success(string.Empty)),
            _ => Task.FromResult(OperationResult.Success("vpn disabled")),
            _ => Task.FromResult(OperationResult.Success("session started")),
            () =>
            {
                rolledBack = true;
                return Task.FromResult(OperationResult.Success("vpn restored"));
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("session started", result.Message);
        Assert.False(rolledBack);
    }
}
