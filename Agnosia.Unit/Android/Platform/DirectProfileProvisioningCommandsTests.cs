using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.Android.Platform;

public sealed class DirectProfileProvisioningCommandsTests
{
    [Theory]
    [InlineData("Success: created user id 12\n", 12)]
    [InlineData("Success: created user id 0", null)]
    [InlineData("Success: created user id -1", null)]
    [InlineData("Success: created user id 2147483648", null)]
    [InlineData("Error: limit 12", null)]
    [InlineData("Success: created user id 12\nError: failed", null)]
    [InlineData("Success: created user id 12 trailing", null)]
    public void Created_user_requires_an_unambiguous_success_response(string output, int? expected)
    {
        Assert.Equal(expected, DirectProfileProvisioningCommands.ParseCreatedUserId(output));
    }

    [Fact]
    public void User_dump_preserves_managed_type_parent_and_serial_for_recovery()
    {
        var users = DirectProfileProvisioningCommands.ParseUsers("""
            Current user: 0
            Users:
              UserInfo{0:Owner:c13} serialNo=0 isPrimary=true
                Type: android.os.usertype.full.SYSTEM
              UserInfo{12:Agnosia:1030} serialNo=19 isPrimary=false parentId=0
                Type: android.os.usertype.profile.MANAGED
              UserInfo{13:Private space:1090} serialNo=21 isPrimary=false parentId=0
                Type: android.os.usertype.profile.PRIVATE
            """);
        Assert.Equal(3, users.Count);
        Assert.Equal(new DirectProfileUser(12, 19, 0, true, true), users[1]);
        Assert.False(users[2].IsManaged);
    }

    [Theory]
    [InlineData("Permission Denial")]
    [InlineData("Users:\n UserInfo{12:Work:1030} serialNo=19")]
    [InlineData("Users:\n UserInfo{12:Work:1030} serialNo=19 parentId=0\n UserInfo{12:Other:1030} serialNo=20 parentId=0")]
    public void Unrecognized_or_ambiguous_user_dump_is_rejected(string output)
    {
        Assert.Throws<FormatException>(() => DirectProfileProvisioningCommands.ParseUsers(output));
    }

    [Fact]
    public void Owner_list_matches_exact_user_and_expands_short_component()
    {
        var owners = DirectProfileProvisioningCommands.ParseOwners("""
            2 owners:
            User  0: admin=com.example/.Admin,DeviceOwner
            User 12: admin=com.agnosia.app/.AgnosiaDeviceAdminReceiver,ProfileOwner,Affiliated
            """);
        Assert.Equal("com.agnosia.app/com.agnosia.app.AgnosiaDeviceAdminReceiver", owners[12]);
        Assert.Empty(DirectProfileProvisioningCommands.ParseOwners("no owners\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown command: list-owners")]
    [InlineData("2 owners:\nUser 12: admin=com.example/.Admin,ProfileOwner")]
    public void Unknown_owner_state_is_not_treated_as_no_owner(string output)
    {
        Assert.Throws<FormatException>(() => DirectProfileProvisioningCommands.ParseOwners(output));
    }

    [Fact]
    public void Shell_arguments_are_quoted_and_negative_user_ids_are_rejected()
    {
        Assert.Equal("'a'\"'\"'b $(id)'", DirectProfileProvisioningCommands.Quote("a'b $(id)"));
        Assert.Equal("pm create-user --profileOf 0 --managed 'Agnosia'",
            DirectProfileProvisioningCommands.CreateUser(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectProfileProvisioningCommands.CreateUser(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectProfileProvisioningCommands.InstallExisting(0, "com.agnosia.app"));
    }

    [Theory]
    [InlineData("pm create-user --profileOf 0 --managed 'Agnosia'", "/system/bin/cmd package create-user --profileOf 0 --managed 'Agnosia'")]
    [InlineData("dpm list-owners", "/system/bin/cmd device_policy list-owners")]
    [InlineData("am start-user -w 12", "/system/bin/cmd activity start-user -w 12")]
    [InlineData("dumpsys user", "/system/bin/dumpsys user")]
    [InlineData("id -u", "/system/bin/id -u")]
    public void Root_deadline_executes_native_command_without_an_intermediate_android_shell_script(string command, string expected)
    {
        Assert.Equal(expected, DirectProfileProvisioningCommands.AsNativeExecutable(command));
    }

    [Theory]
    [InlineData("Broadcast completed: result=0", false)]
    [InlineData("Broadcast completed: result=-1", true)]
    [InlineData("Broadcast completed: result=-10", false)]
    public void Broadcast_requires_acknowledgement_from_the_setup_receiver(string output, bool expected)
    {
        Assert.Equal(expected, DirectProfileProvisioningCommands.IsSetupAcknowledged(output));
    }
}
