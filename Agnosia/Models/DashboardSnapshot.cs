namespace Agnosia.Models;

public sealed record DashboardSnapshot(
    bool IsSupported,
    bool HasSetup,
    bool IsSettingUp,
    bool WorkProfileAvailable,
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
            PersonalApps: [],
            WorkApps: [],
            Settings: AppSettingsSnapshot.Default);
}
