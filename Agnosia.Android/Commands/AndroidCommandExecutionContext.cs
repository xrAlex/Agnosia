#if AGNOSIA_ANDROID
using Android.App;
using Android.App.Admin;
using Android.Content;
#endif

namespace Agnosia.Android.Commands;

internal sealed record AndroidCommandExecutionContext(
#if AGNOSIA_ANDROID
    Context Context,
    Activity? Activity,
    DevicePolicyManager? PolicyManager,
    ComponentName? Admin,
#endif
    AndroidCommandTargetProfile RequestedProfile,
    AndroidCommandExecutionProfile ActualProfile,
    AndroidCommandTransportKind Transport,
    string ContextSource)
{
#if !AGNOSIA_ANDROID
    public static AndroidCommandExecutionContext ForTests(
        AndroidCommandTransportKind transport,
        AndroidCommandTargetProfile requestedProfile,
        AndroidCommandExecutionProfile actualProfile,
        string contextSource)
    {
        return new AndroidCommandExecutionContext(
            requestedProfile,
            actualProfile,
            transport,
            contextSource);
    }
#endif
}
