using Agnosia.Android.Infrastructure;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private void ActionSetCrossProfileInteraction()
    {
        if (!_isProfileOwner || _policyManager is null)
        {
            FinishWithToggleResult(false);
            return;
        }

        var packages = Intent?.GetStringArrayExtra("packages") ?? [];
        FinishWithToggleResult(AndroidPolicyApi.TrySetCrossProfilePackages(
            _policyManager,
            AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType),
            packages,
            LogTag));
    }

    private void ActionSynchronizePreference()
    {
        var intent = Intent;
        var name = intent?.GetStringExtra("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            Finish();
            return;
        }

        if (intent?.HasExtra("boolean") == true)
        {
            var booleanValue = intent.GetBooleanExtra("boolean", false);
            ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(name, booleanValue);
            if (string.Equals(name, StorageKeys.LoggingEnabled, StringComparison.Ordinal) && !booleanValue)
                AndroidAppLogArchive.Clear(this);
        }
        else if (intent?.HasExtra("int") == true)
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().SetInt(name, intent.GetIntExtra("int", int.MinValue));
        }

        if (_isProfileOwner)
            AndroidStartup.EnforceWorkProfilePolicies(this);

        FinishWithResult(Result.Ok);
    }

    private void ActionFinalizeProvision()
    {
        if (_isProfileOwner)
        {
            Finish();
            return;
        }

        ServiceRegistry.GetRequiredService<AndroidPlatformBridge>().NotifyManagedProfileProvisioned(this, Intent);

        var launchIntent = string.IsNullOrWhiteSpace(PackageName)
            ? null
            : PackageManager?.GetLaunchIntentForPackage(PackageName);
        if (launchIntent is not null)
        {
            launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            AndroidIntentApi.TryStartActivity(
                this,
                launchIntent,
                LogTag,
                "Android не смог открыть Agnosia после завершения настройки.",
                out _);
        }

        Toast.MakeText(this, "Настройка Agnosia завершена.", ToastLength.Long)?.Show();
        Finish();
    }
}
