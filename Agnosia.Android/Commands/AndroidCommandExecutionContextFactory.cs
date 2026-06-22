#if AGNOSIA_ANDROID
using Agnosia.Android.Receivers;
using Android.App;
using Android.Content;

namespace Agnosia.Android.Commands;

internal sealed class AndroidCommandExecutionContextFactory(Context applicationContext)
{
    public AndroidCommandExecutionContext Create(
        Context context,
        Activity? activity,
        AndroidCommandEnvelope envelope,
        AndroidCommandTransportKind transport,
        string contextSource)
    {
        var appContext = context.ApplicationContext ?? applicationContext.ApplicationContext ?? context;
        var policyManager = AndroidSystemApi.GetDevicePolicyManager(appContext);
        var admin = AgnosiaUtilities.GetAdminComponent(appContext, typeof(AgnosiaDeviceAdminReceiver));
        var actualProfile = ResolveActualProfile(appContext);

        return new AndroidCommandExecutionContext(
            appContext,
            activity,
            policyManager,
            admin,
            envelope.TargetProfile,
            actualProfile,
            transport,
            contextSource);
    }

    private static AndroidCommandExecutionProfile ResolveActualProfile(Context context)
    {
        try
        {
            return AgnosiaUtilities.IsProfileOwner(context)
                ? AndroidCommandExecutionProfile.Work
                : AndroidCommandExecutionProfile.Personal;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return AndroidCommandExecutionProfile.Unknown;
        }
    }
}
#endif
