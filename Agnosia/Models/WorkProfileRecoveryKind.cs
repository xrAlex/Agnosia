namespace Agnosia.Models;

public enum WorkProfileRecoveryKind
{
    None,
    WorkProfileQuietMode,
    WorkProfileUnavailable,
    WorkProfileCommandTargetUnavailable,
    WorkProfileCommandChannelUnavailable,
    WorkProfileCreatedButAppNotReady,
    AppInstalledInWorkProfileButNotOwner,
    ForeignProfileOwner,
    ErrorUnknownWithDiagnostics
}
