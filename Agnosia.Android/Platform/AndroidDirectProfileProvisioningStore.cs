namespace Agnosia.Android.Platform;

internal sealed class AndroidDirectProfileProvisioningStore(LocalStorageManager storage, int bootCount) : IDirectProfileProvisioningStore
{
    public bool DidWrite { get; private set; }

    public DirectProfileProvisioningState? State => storage.GetString(StorageKeys.DirectProfileProvisioning) is { } value
        ? DirectProfileProvisioningState.Decode(value) : null;

    public string? Key => storage.GetString(StorageKeys.AuthKey);
    public int BootCount => bootCount;

    public void Save(DirectProfileProvisioningState state, string key)
    {
        var complete = state.Stage == DirectProfileProvisioningStage.Complete;
        if (state.UserId < 0) AgnosiaUtilities.MarkWorkProfileSetupStarted();
        var values = new Dictionary<string, string>
        {
            [StorageKeys.DirectProfileProvisioning] = state.Encode(),
            [StorageKeys.AuthKey] = key
        };
        if (state.UserSerial >= 0)
        {
            values[StorageKeys.ManagedProfileUserHandle] = FormattableString.Invariant($"UserHandle{{{state.UserId}}}");
            storage.SetLong(StorageKeys.ManagedProfileUserSerial, state.UserSerial);
            storage.SetLong(StorageKeys.ManagedProfileProvisionedAtUtc, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        if (!complete) storage.SetLong(StorageKeys.SetupStartedAtUtc, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        storage.SetValues(new Dictionary<string, bool>
        {
            [StorageKeys.IsSettingUp] = !complete,
            [StorageKeys.HasSetup] = complete,
            [StorageKeys.OnboardingCompleted] = false
        }, values);
        DidWrite = true;
    }
}
