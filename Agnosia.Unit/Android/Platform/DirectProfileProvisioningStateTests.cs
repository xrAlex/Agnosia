using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.Android.Platform;

public sealed class DirectProfileProvisioningStateTests
{
    [Theory]
    [InlineData("1|0|0|12|19|CreatingProfile", true)]
    [InlineData("1|0|0|12|19|ApplyingPolicies", true)]
    [InlineData("1|0|0|12|19|VerifyingConnection", true)]
    [InlineData("1|0|0|12|19|AwaitingReboot|7", true)]
    [InlineData("1|0|0|12|19|Complete", false)]
    [InlineData("corrupted", true)]
    [InlineData(null, false)]
    public void Pending_or_corrupt_journal_blocks_readiness_until_policies_and_connection_are_confirmed(string? encoded, bool expected)
    {
        Assert.Equal(expected, DirectProfileProvisioningState.BlocksReadiness(encoded));
    }

    [Fact]
    public void Durable_checkpoint_retains_both_serials_and_stage()
    {
        var state = DirectProfileProvisioningState.Decode("1|10|15|12|19|AssigningOwner");
        Assert.Equal(10, state.ParentId);
        Assert.Equal(15, state.ParentSerial);
        Assert.Equal(12, state.UserId);
        Assert.Equal(19, state.UserSerial);
        Assert.Equal(DirectProfileProvisioningStage.AssigningOwner, state.Stage);
        Assert.Equal("1|10|15|12|19|AssigningOwner", state.Encode());
    }

    [Fact]
    public void Unknown_outcome_retains_boot_counter_across_process_restart()
    {
        var state = DirectProfileProvisioningState.Decode("1|0|0|12|19|AwaitingReboot|7");
        Assert.Equal(7, state.UncertainBootCount);
        Assert.Equal("1|0|0|12|19|AwaitingReboot|7", state.Encode());
    }

    [Theory]
    [InlineData("1|0|0|12|19|AwaitingReboot|7", 7, true)]
    [InlineData("1|0|0|12|19|AwaitingReboot|7", -1, true)]
    [InlineData("1|0|0|12|19|AwaitingReboot|7", 8, false)]
    [InlineData("1|0|0|12|19|AwaitingReboot", 8, true)]
    [InlineData("1|0|0|12|19|CreatingProfile", 7, false)]
    [InlineData("1|0|0|12|19|Complete", 7, false)]
    public void All_provisioning_paths_share_the_reboot_barrier(string encoded, int bootCount, bool expected)
    {
        Assert.Equal(expected, DirectProfileProvisioningState.Decode(encoded).RequiresReboot(bootCount));
    }
}
