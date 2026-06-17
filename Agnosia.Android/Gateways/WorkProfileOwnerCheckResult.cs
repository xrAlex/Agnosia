namespace Agnosia.Android.Gateways;

internal enum WorkProfileOwnerCheckKind
{
    AuthenticationKeyMissing,
    TargetUnavailable,
    Unreachable,
    VersionUpdateFailed,
    AppInstalledButNotOwner,
    AppIsProfileOwner
}

internal sealed record WorkProfileOwnerCheckResult(
    WorkProfileOwnerCheckKind Kind,
    string DiagnosticReason,
    long AppVersionCode = 0,
    string? AppVersionName = null);
