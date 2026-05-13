namespace Agnosia.Models;

public enum WorkProfileStateKind
{
    NoWorkProfile,
    ProvisioningInProgress,
    WorkProfileCreatedButAppNotReady,
    AppInstalledInWorkProfileButNotOwner,
    AppIsProfileOwner,
    ForeignProfileOwner,
    ErrorUnknownWithDiagnostics
}
