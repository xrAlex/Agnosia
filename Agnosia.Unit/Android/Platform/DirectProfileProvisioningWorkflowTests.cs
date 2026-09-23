using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.Android.Platform;

public sealed class DirectProfileProvisioningWorkflowTests
{
    private const string Key = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string Admin = "com.agnosia.app/com.agnosia.app.AgnosiaDeviceAdminReceiver";
    private const string Parent = "Users:\n  UserInfo{0:Owner:c13} serialNo=0 isPrimary=true\n";
    private const string Profile = "  UserInfo{12:Agnosia:1030} serialNo=19 isPrimary=false parentId=0\n";

    [Fact]
    public async Task Root_denial_does_not_create_a_user_or_replace_authentication()
    {
        var shell = new Shell { Root = false };
        var store = new Store();
        var result = await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Contains("root", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["id -u"], shell.Commands);
        Assert.Null(store.State);
        Assert.Null(store.Key);
    }

    [Fact]
    public async Task Existing_managed_profile_is_never_adopted()
    {
        var shell = new Shell { HasProfile = true };
        var store = new Store();
        var result = await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("pm create-user"));
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("dpm set-profile-owner"));
        Assert.Null(store.Key);
    }

    [Fact]
    public async Task Success_requires_persisted_identity_owner_policies_and_signed_connection()
    {
        var shell = new Shell();
        var store = new Store();
        var confirmed = false;
        var workflow = Create(shell, store, (user, _) =>
        {
            Assert.Equal(12, user.Id);
            Assert.Equal(19, user.Serial);
            Assert.Equal(DirectProfileProvisioningStage.VerifyingConnection, store.State!.Stage);
            Assert.Contains(shell.Commands, command => command.StartsWith("am broadcast"));
            confirmed = true;
            return Task.FromResult(true);
        });
        shell.BeforeCreate = () => Assert.Equal(DirectProfileProvisioningStage.AwaitingReboot, store.State!.Stage);
        shell.BeforeInstall = () => Assert.Equal(19, store.State!.UserSerial);
        var result = await workflow.RunAsync(0, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(confirmed);
        Assert.Equal(DirectProfileProvisioningStage.Complete, store.State!.Stage);
        Assert.NotNull(store.Key);
    }

    [Theory]
    [InlineData("pm install-existing")]
    [InlineData("dpm set-profile-owner")]
    [InlineData("am start-user")]
    [InlineData("am broadcast")]
    public async Task Failure_preserves_identity_and_retry_does_not_create_another_user(string failedCommand)
    {
        var shell = new Shell { FailPrefix = failedCommand };
        var store = new Store();
        Assert.False((await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(12, store.State!.UserId);
        Assert.StartsWith(failedCommand, shell.Commands[^1]);
        var key = store.Key;
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("pm remove-user"));
        shell.FailPrefix = null;
        Assert.True((await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(key, store.Key);
        Assert.Single(shell.Commands, command => command.StartsWith("pm create-user"));
    }

    [Fact]
    public async Task Reused_numeric_user_id_is_rejected_before_installation()
    {
        var shell = new Shell { HasProfile = true, Serial = 20 };
        var store = Pending();
        var result = await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("pm install"));
        Assert.Equal(Key, store.Key);
    }

    [Fact]
    public async Task Other_owner_is_rejected_without_reassigning_or_installing()
    {
        var shell = new Shell { HasProfile = true, Owner = "com.other/.Admin" };
        var result = await Create(shell, Pending()).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("pm install") || command.StartsWith("dpm set"));
    }

    [Fact]
    public async Task Completed_profile_is_not_reprovisioned_when_temporarily_unavailable()
    {
        var shell = new Shell { HasProfile = true, Owner = Admin };
        var store = Pending();
        store.State = store.State! with { Stage = DirectProfileProvisioningStage.Complete };
        var result = await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal(DirectProfileProvisioningStage.Complete, store.State.Stage);
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("pm install")
                                                        || command.StartsWith("pm create")
                                                        || command.StartsWith("dpm set")
                                                        || command.StartsWith("am "));
        Assert.Equal(Key, store.Key);
    }

    [Fact]
    public async Task Deleted_completed_profile_allows_fresh_creation_with_a_new_key()
    {
        var shell = new Shell { Serial = 20 };
        var store = Pending();
        store.State = store.State! with { Stage = DirectProfileProvisioningStage.Complete };
        var result = await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(20, store.State.UserSerial);
        Assert.NotEqual(Key, store.Key);
        Assert.Single(shell.Commands, command => command.StartsWith("pm create-user"));
    }

    [Fact]
    public async Task Process_death_during_mutation_leaves_a_durable_barrier_against_concurrent_retry()
    {
        var shell = new Shell { SimulateProcessDeath = true };
        var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken));
        Assert.Equal(DirectProfileProvisioningStage.AwaitingReboot, store.State!.Stage);
        shell.SimulateProcessDeath = false;
        Assert.False((await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Single(shell.Commands, command => command.StartsWith("pm create-user"));
    }

    [Fact]
    public async Task Unconfirmed_root_termination_blocks_retry_until_reboot()
    {
        var shell = new Shell { UnknownOutcomePrefix = "dpm set-profile-owner" };
        var store = new Store();
        Assert.False((await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(DirectProfileProvisioningStage.AwaitingReboot, store.State!.Stage);
        var mutationCount = shell.Commands.Count(command => command.StartsWith("pm install"));
        shell.UnknownOutcomePrefix = null;
        Assert.False((await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(mutationCount, shell.Commands.Count(command => command.StartsWith("pm install")));
        store.BootCount++;
        Assert.True((await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Single(shell.Commands, command => command.StartsWith("pm create-user"));
    }

    [Fact]
    public async Task Lost_create_response_requires_recovery_instead_of_creating_duplicate()
    {
        var shell = new Shell { HasProfile = true };
        var store = new Store
        {
            Key = Key,
            State = new(0, 0, -1, -1, DirectProfileProvisioningStage.CreatingProfile)
        };
        Assert.False((await Create(shell, store).RunAsync(0, TestContext.Current.CancellationToken)).Succeeded);
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("pm create-user"));
    }

    [Fact]
    public async Task Shell_success_without_receiver_acknowledgement_is_a_failure()
    {
        var shell = new Shell { Acknowledged = false };
        var confirmed = false;
        var result = await Create(shell, new Store(), (_, _) =>
        {
            confirmed = true;
            return Task.FromResult(true);
        }).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.False(confirmed);
    }

    [Fact]
    public async Task Failed_signed_connection_does_not_complete_setup()
    {
        var store = new Store();
        var result = await Create(new Shell(), store, (_, _) => Task.FromResult(false)).RunAsync(0, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.NotEqual(DirectProfileProvisioningStage.Complete, store.State!.Stage);
    }

    [Fact]
    public async Task Cancellation_after_creation_retains_recoverable_state()
    {
        using var cancellation = new CancellationTokenSource();
        var shell = new Shell { BeforeInstall = cancellation.Cancel };
        var store = new Store();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(shell, store).RunAsync(0, cancellation.Token));
        Assert.Equal(12, store.State!.UserId);
        Assert.DoesNotContain(shell.Commands, command => command.StartsWith("dpm set"));
    }

    private static Store Pending() => new()
    {
        Key = Key,
        State = new(0, 0, 12, 19, DirectProfileProvisioningStage.InstallingPackage)
    };

    private static DirectProfileProvisioningWorkflow Create(Shell shell, Store store,
        Func<DirectProfileUser, CancellationToken, Task<bool>>? confirm = null) =>
        new(shell, store, "com.agnosia.app", Admin, confirm ?? ((_, _) => Task.FromResult(true)));

    private sealed class Store : IDirectProfileProvisioningStore
    {
        public DirectProfileProvisioningState? State { get; set; }
        public string? Key { get; set; }
        public int BootCount { get; set; } = 1;
        public void Save(DirectProfileProvisioningState state, string key) { State = state; Key = key; }
    }

    // Only the external Android shell is simulated; all ordering and recovery decisions run in production code.
    private sealed class Shell : IRootCommandRunner
    {
        public bool Root { get; init; } = true;
        public bool HasProfile { get; set; }
        public long Serial { get; init; } = 19;
        public string? Owner { get; set; }
        public bool Acknowledged { get; init; } = true;
        public string? FailPrefix { get; set; }
        public string? UnknownOutcomePrefix { get; set; }
        public bool SimulateProcessDeath { get; set; }
        public Action? BeforeCreate { get; set; }
        public Action? BeforeInstall { get; set; }
        public List<string> Commands { get; } = [];

        public Task<RootCommandResult> RunAsync(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (UnknownOutcomePrefix is not null && command.StartsWith(UnknownOutcomePrefix))
                throw new RootCommandOutcomeUnknownException();
            if (FailPrefix is not null && command.StartsWith(FailPrefix))
                return Task.FromResult(new RootCommandResult(1, "", "denied"));
            string output;
            if (command == "id -u") output = Root ? "0" : "2000";
            else if (command == "dumpsys user") output = Parent + (HasProfile ? Profile.Replace("serialNo=19", $"serialNo={Serial}") : "");
            else if (command == "dpm list-owners") output = Owner is null ? "no owners" : $"1 owner:\nUser 12: admin={Owner},ProfileOwner";
            else if (command.StartsWith("pm create-user"))
            {
                BeforeCreate?.Invoke();
                HasProfile = true;
                if (SimulateProcessDeath) throw new InvalidOperationException("Simulated process death.");
                output = "Success: created user id 12";
            }
            else if (command.StartsWith("pm install-existing"))
            {
                BeforeInstall?.Invoke();
                output = "Package com.agnosia.app installed for user: 12";
            }
            else if (command.StartsWith("pm path")) output = "package:/data/app/com.agnosia.app/base.apk";
            else if (command.StartsWith("dpm set-profile-owner"))
            {
                Owner = Admin;
                output = "Success: Active admin and profile owner set to com.agnosia.app/.AgnosiaDeviceAdminReceiver for user 12";
            }
            else if (command.StartsWith("am start-user")) output = "Success: user started";
            else if (command.StartsWith("am broadcast")) output = $"Broadcast completed: result={(Acknowledged ? -1 : 0)}";
            else throw new InvalidOperationException("Unexpected command: " + command);
            return Task.FromResult(new RootCommandResult(0, output, ""));
        }
    }
}
