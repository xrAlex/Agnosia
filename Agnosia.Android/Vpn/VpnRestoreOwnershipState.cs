namespace Agnosia.Android.Vpn;

internal sealed record VpnRestoreOwner
{
    public string LaunchId { get; }
    public string PackageName { get; }

    public VpnRestoreOwner(string launchId, string packageName)
    {
        if (string.IsNullOrWhiteSpace(launchId))
            throw new ArgumentException("Launch identity is required.", nameof(launchId));
        if (string.IsNullOrWhiteSpace(packageName))
            throw new ArgumentException("Package name is required.", nameof(packageName));

        LaunchId = launchId;
        PackageName = packageName;
    }
}

internal sealed record VpnRestoreOwnershipState(
    bool RestoreRequired,
    VpnRestoreOwner? ActiveOwner,
    VpnRestoreOwner? PendingOwner,
    bool AcceptLegacyCallback = false,
    bool RestoreReady = false,
    int Version = VpnRestoreOwnershipState.CurrentVersion,
    bool ForceRestore = false,
    bool PendingLaunchDispatched = false)
{
    public const int CurrentVersion = 2;

    public static VpnRestoreOwnershipState Empty { get; } = new(false, null, null);
    public static VpnRestoreOwnershipState Legacy { get; } = new(true, null, null, true);

    public VpnRestoreOwnershipState Begin(VpnRestoreOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (PendingOwner is not null)
            throw new InvalidOperationException("A VPN restore launch is already pending.");

        return this with { PendingOwner = owner, PendingLaunchDispatched = false };
    }

    public VpnRestoreOwnershipState RequireRestore()
    {
        if (PendingOwner is null)
            throw new InvalidOperationException("A pending launch is required before claiming VPN restore ownership.");

        return this with { RestoreRequired = true };
    }

    public VpnRestoreOwnershipState Commit(VpnRestoreOwner owner)
    {
        EnsurePendingOwner(owner);
        return RestoreRequired
            ? this with
            {
                ActiveOwner = owner,
                PendingOwner = null,
                PendingLaunchDispatched = false,
                AcceptLegacyCallback = false,
                RestoreReady = false
            }
            : this with { PendingOwner = null, PendingLaunchDispatched = false };
    }

    public VpnRestoreOwnershipState Abort(VpnRestoreOwner owner)
    {
        EnsurePendingOwner(owner);
        return this with { PendingOwner = null, PendingLaunchDispatched = false };
    }

    public bool MatchesCallback(string packageName, string? launchId)
    {
        if (!RestoreRequired || string.IsNullOrWhiteSpace(packageName)) return false;
        if (string.IsNullOrWhiteSpace(launchId))
            return AcceptLegacyCallback && ActiveOwner is null;

        return (PendingOwner ?? ActiveOwner) is { } active
               && string.Equals(active.PackageName, packageName, StringComparison.Ordinal)
               && string.Equals(active.LaunchId, launchId, StringComparison.Ordinal);
    }

    public VpnRestoreOwnershipState MarkRestoreReady(string packageName, string? launchId)
    {
        return MatchesCallback(packageName, launchId)
            ? this with { ActiveOwner = PendingOwner ?? ActiveOwner, PendingOwner = null,
                PendingLaunchDispatched = false, RestoreReady = true }
            : this;
    }

    public VpnRestoreOwnershipState ClearAfterRestore()
    {
        return Empty;
    }

    private void EnsurePendingOwner(VpnRestoreOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!Equals(PendingOwner, owner))
            throw new InvalidOperationException("The pending VPN restore owner does not match the launch.");
    }
}
