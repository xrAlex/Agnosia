using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Packages;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private void FinishWithSystemAppError()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultError, AndroidCommandContract.ErrorSystemAppUnsupported);
        FinishWithResult(Result.Canceled, result);
    }

    private void FinishWithError(string message)
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultError, message);
        FinishWithResult(Result.Canceled, result);
    }

    private void FinishWithSuccessMessage(string message)
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultMessage, message);
        FinishWithResult(Result.Ok, result);
    }

    private void FinishWithToggleResult(bool success)
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultToggleSuccess, success);
        FinishWithResult(success ? Result.Ok : Result.Canceled, result);
    }

    private static string FormatPermissionName(string permission)
    {
        const string androidPermissionPrefix = "android.permission.";
        return permission.StartsWith(androidPermissionPrefix, StringComparison.Ordinal)
            ? permission[androidPermissionPrefix.Length..]
            : permission;
    }

    private void FinishWithResult(Result resultCode, Intent? data = null)
    {
        if (_finishRequested || _destroyCancellation.IsCancellationRequested) return;

        _finishRequested = true;
        Log.Debug(
            LogTag,
            $"Finishing action={Intent?.Action ?? "<none>"} with result={resultCode}, hasData={data is not null}.");
        if (data is null)
            SetResult(resultCode);
        else
            SetResult(resultCode, data);

        Finish();
    }

    internal void HandlePackageInstallerCallback(Intent? intent)
    {
        RunAction(
            cancellationToken => HandlePackageInstallerCallbackAsync(intent, cancellationToken),
            "Android не смог обработать результат установки пакета.");
    }

    private async Task HandlePackageInstallerCallbackAsync(
        Intent? intent,
        CancellationToken cancellationToken)
    {
        var status = (PackageInstallStatus)(intent?.Extras?.GetInt(PackageInstaller.ExtraStatus) ??
                                            (int)PackageInstallStatus.Failure);
        var callbackPackage = intent?.GetStringExtra(AndroidCommandContract.ExtraCallbackPackage)
                              ?? intent?.GetStringExtra(PackageInstaller.ExtraPackageName);
        var operation = intent?.GetStringExtra(AndroidCommandContract.ExtraPackageInstallerOperation);
        var statusMessage = intent?.GetStringExtra(PackageInstaller.ExtraStatusMessage);

        Log.Info(LogTag,
            $"PackageInstaller callback status={status}, operation={operation ?? "<unknown>"}, package={callbackPackage ?? "<unknown>"}, statusMessage={statusMessage ?? "<none>"}.");

        if (status == PackageInstallStatus.PendingUserAction)
        {
            var confirmationIntent = (Intent?)intent?.Extras?.Get(Intent.ExtraIntent);
            if (confirmationIntent is not null)
            {
                try
                {
                    StartActivityForResult(confirmationIntent, PackageInstallerUserActionRequestCode);
                }
                catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
                {
                    Log.Warn(LogTag, $"Android не смог открыть подтверждение установки пакета. Details: {exception}");
                    FinishWithError("Android не смог открыть подтверждение установки пакета.");
                }

                return;
            }

            FinishWithError("Android запросил подтверждение установки, но не предоставил экран подтверждения.");
            return;
        }

        if (status == PackageInstallStatus.Success)
        {
            if (string.Equals(operation, AndroidCommandContract.PackageInstallerOperationInstall,
                    StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(callbackPackage)
                && !await WaitForPackageAvailableAsync(callbackPackage, cancellationToken))
            {
                FinishWithError($"Android установил {callbackPackage}, но пакет еще не доступен в рабочем профиле.");
                return;
            }

            FinishWithResult(Result.Ok);
            return;
        }

        FinishWithError(string.IsNullOrWhiteSpace(statusMessage)
            ? "Android отклонил установку пакета."
            : $"Android отклонил установку пакета: {statusMessage}");
    }

    private void DeliverPendingPackageInstallerCallback()
    {
        if (PackageInstallerCallbackCoordinator.TakePendingCallback() is { } pendingCallback)
            HandlePackageInstallerCallback(pendingCallback);
    }
}
