namespace Agnosia.Android.Commands;

internal sealed record PackageStateResult(
    string PackageName,
    bool Installed,
    bool Hidden,
    string? LaunchId = null,
    bool RecoveryAcknowledged = false);
