namespace Agnosia.Platform;

public interface IPlatformBridge :
    IDashboardPlatformService,
    IPlatformEventLogReader,
    IPermissionPlatformService,
    IOnboardingPlatformService,
    IAppCommandService,
    ISettingsPlatformService
{
}
