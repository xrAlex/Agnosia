using Agnosia.Android.Api;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Android.Content.PM;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Activities;

[Activity(
    Name = "com.agnosia.app.WorkProfileBootstrapActivity",
    Label = "@string/app_name",
    Theme = "@android:style/Theme.Translucent.NoTitleBar",
    Exported = true,
    ExcludeFromRecents = true,
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTask)]
public sealed class WorkProfileBootstrapActivity : Activity
{
    private const string LogTag = "AgnosiaWorkBootstrap";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AgnosiaRuntime.Initialize(this);

        if (!AgnosiaUtilities.IsProfileOwner(this))
        {
            Finish();
            return;
        }

        try
        {
            AgnosiaUtilities.EnforceWorkProfilePolicies(
                this,
                typeof(AgnosiaDeviceAdminReceiver),
                typeof(MainActivity),
                enableProfile: true);
            AgnosiaUtilities.EnforceUserRestrictions(this, typeof(AgnosiaDeviceAdminReceiver));
            WorkProfileLockFreezeService.EnsureRunning(this);
            Log.Info(LogTag, "Work-profile bootstrap policies applied.");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Work-profile bootstrap failed: {exception}");
        }
        finally
        {
            Finish();
        }
    }
}
