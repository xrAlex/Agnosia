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
            false,
            false,
            false,
            false,
            WorkProfileStateKind.NoWorkProfile,
            WorkProfileRecoveryKind.None,
            string.Empty,
            [],
            [],
            AppSettingsSnapshot.Default);
}