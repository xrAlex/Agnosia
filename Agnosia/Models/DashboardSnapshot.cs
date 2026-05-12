namespace Agnosia.Models;

public sealed record DashboardSnapshot(
    bool IsSupported,
    bool HasSetup,
    bool IsSettingUp,
    bool WorkProfileAvailable,
    WorkProfileRecoveryKind WorkProfileRecovery,
    IReadOnlyList<AppSnapshot> PersonalApps,
    IReadOnlyList<AppSnapshot> WorkApps,
    AppSettingsSnapshot Settings)
{
    public static DashboardSnapshot Unsupported { get; } =
        new(
            IsSupported: false,
            HasSetup: false,
            IsSettingUp: false,
            WorkProfileAvailable: false,
            WorkProfileRecovery: WorkProfileRecoveryKind.None,
            PersonalApps: [],
            WorkApps: [],
            Settings: AppSettingsSnapshot.Default);
}
