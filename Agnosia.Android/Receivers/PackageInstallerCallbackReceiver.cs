using Agnosia.Android.Activities;
using Agnosia.Android.Api;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(
    Name = "com.agnosia.app.PackageInstallerCallbackReceiver",
    Exported = false)]
[IntentFilter(
[
    AgnosiaActions.PackageInstallerCallback
], Categories = [Intent.CategoryDefault])]
public sealed class PackageInstallerCallbackReceiver : BroadcastReceiver
{
    private const string LogTag = "AgnosiaPkgCallback";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent is null)
            return;

        if (context is not null)
        {
            AgnosiaRuntime.Initialize(context);
        }

        var status = (PackageInstallStatus)(intent.Extras?.GetInt(PackageInstaller.ExtraStatus) ?? (int)PackageInstallStatus.Failure);
        Log.Info(LogTag, $"Broadcast callback status={status}.");

        DummyActivity.DispatchPackageInstallerCallback(intent);
    }
}
