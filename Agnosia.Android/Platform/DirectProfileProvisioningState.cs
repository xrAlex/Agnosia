using System.Globalization;

namespace Agnosia.Android.Platform;

internal enum DirectProfileProvisioningStage
{
    CreatingProfile,
    InstallingPackage,
    AssigningOwner,
    StartingProfile,
    ApplyingPolicies,
    VerifyingConnection,
    Complete,
    AwaitingReboot
}

internal sealed record DirectProfileProvisioningState(
    int ParentId, long ParentSerial, int UserId, long UserSerial, DirectProfileProvisioningStage Stage,
    int? UncertainBootCount = null)
{
    public string Encode() => FormattableString.Invariant($"1|{ParentId}|{ParentSerial}|{UserId}|{UserSerial}|{Stage}")
                              + (UncertainBootCount is { } boot ? "|" + boot.ToString(CultureInfo.InvariantCulture) : "");

    public bool RequiresReboot(int bootCount) => Stage == DirectProfileProvisioningStage.AwaitingReboot
                                               && (UncertainBootCount is null || bootCount <= UncertainBootCount);

    public static bool BlocksReadiness(string? value)
    {
        if (value is null) return false;
        try { return Decode(value).Stage != DirectProfileProvisioningStage.Complete; }
        catch (FormatException) { return true; }
    }

    public static DirectProfileProvisioningState Decode(string value)
    {
        var parts = value.Split('|');
        if (parts.Length is not (6 or 7) || parts[0] != "1"
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var parent) || parent < 0
            || !long.TryParse(parts[2], CultureInfo.InvariantCulture, out var parentSerial) || parentSerial < 0
            || !int.TryParse(parts[3], CultureInfo.InvariantCulture, out var user) || user < -1 || user == 0
            || !long.TryParse(parts[4], CultureInfo.InvariantCulture, out var serial) || serial < -1
            || !Enum.TryParse<DirectProfileProvisioningStage>(parts[5], out var stage) || !Enum.IsDefined(stage))
            throw new FormatException("Invalid direct provisioning state.");
        int? boot = null;
        if (parts.Length == 7)
        {
            if (!int.TryParse(parts[6], CultureInfo.InvariantCulture, out var count) || count < 0)
                throw new FormatException("Invalid boot count.");
            boot = count;
        }
        return new(parent, parentSerial, user, serial, stage, boot);
    }
}

internal interface IDirectProfileProvisioningStore
{
    DirectProfileProvisioningState? State { get; }
    string? Key { get; }
    int BootCount { get; }
    void Save(DirectProfileProvisioningState state, string key);
}

internal sealed record RootCommandResult(int ExitCode, string Output, string Error);

// The Android service may have accepted a mutation even if its shell client was killed.
internal sealed class RootCommandOutcomeUnknownException() : IOException("Root command termination was not confirmed.");

internal interface IRootCommandRunner
{
    Task<RootCommandResult> RunAsync(string command, CancellationToken cancellationToken);
}
