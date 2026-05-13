namespace Agnosia.Models;

public sealed record DashboardSnapshot(
    bool IsSupported,
    bool HasSetup,
    bool IsSettingUp,
    bool WorkProfileAvailable,
    WorkProfileStateKind WorkProfileState,
    WorkProfileRecoveryKind WorkProfileRecovery,
    string WorkProfileDiagnosticReason,
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
            WorkProfileState: WorkProfileStateKind.NoWorkProfile,
            WorkProfileRecovery: WorkProfileRecoveryKind.None,
            WorkProfileDiagnosticReason: string.Empty,
            PersonalApps: [],
            WorkApps: [],
            Settings: AppSettingsSnapshot.Default);
}
