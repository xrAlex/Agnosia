namespace Agnosia.Models;

public enum WorkProfileStateKind
{
    NoWorkProfile,
    ProvisioningInProgress,
    WorkProfileQuietMode,
    WorkProfileUnavailable,
    WorkProfileCommandTargetUnavailable,
    WorkProfileCommandChannelUnavailable,
    WorkProfileCreatedButAppNotReady,
    AppInstalledInWorkProfileButNotOwner,
    AppIsProfileOwner,
    ForeignProfileOwner,
    ErrorUnknownWithDiagnostics
}
