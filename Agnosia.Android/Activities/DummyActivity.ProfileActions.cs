using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Infrastructure;
using Agnosia.Android.Vpn;
using Android.App;
using Android.Content;
using Android.Widget;
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
            LocalStorageManager.Instance.SetBoolean(name, booleanValue);
            if (string.Equals(name, StorageKeys.LoggingEnabled, StringComparison.Ordinal) && !booleanValue)
                AndroidAppLogArchive.Clear(this);
        }
        else if (intent?.HasExtra("int") == true)
        {
            LocalStorageManager.Instance.SetInt(name, intent.GetIntExtra("int", int.MinValue));
        }

        if (_isProfileOwner)
            AndroidStartup.EnforceWorkProfilePolicies(this);

        FinishWithResult(Result.Ok);
    }

    private async Task ActionWorkAppFrozenAsync(CancellationToken cancellationToken)
    {
        if (_isProfileOwner)
        {
            Finish();
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trigger = Intent?.GetStringExtra(AndroidProfileCommandGateway.ExtraTrigger) ?? "work_app_frozen";

            var result = await WorkAppFrozenHandler.RestoreParentVpnAndHideOverlayAsync(
                this,
                trigger,
                LogTag,
                cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                FinishWithSuccessMessage(result.Message);
                return;
            }

            Log.Warn(LogTag, $"Work-app frozen event handling failed. trigger={trigger}, message={result.Message}");
            FinishWithError(result.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to handle work-app frozen event: {exception}");
            FinishWithError("Android не смог обработать событие заморозки приложения.");
        }
    }

    private void ActionFinalizeProvision()
    {
        if (_isProfileOwner)
        {
            Finish();
            return;
        }

        AndroidPlatformBridge.Instance.NotifyManagedProfileProvisioned(this, Intent);

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
