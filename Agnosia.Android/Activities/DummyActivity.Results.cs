using Android.Content;
using Android.Content.PM;
using Android.OS;
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

    private void FinishWithError(string message) => FinishWithError(message, null);

    private void FinishWithError(string message, string? errorCode)
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultError, message);
        if (!string.IsNullOrWhiteSpace(errorCode))
            result.PutExtra(AndroidCommandContract.ResultCommandErrorCode, errorCode);
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
        if (Looper.MainLooper?.IsCurrentThread != true)
        {
            RunOnUiThread(() => FinishWithResult(resultCode, data));
            return;
        }
        if (_finishRequested || _destroyCancellation.IsCancellationRequested) return;

        _finishRequested = true;
        ReleasePackageInstallerCallback();
        if (AgnosiaUtilities.IsProfileOwner(this)
            && Intent?.Action == AgnosiaActions.UnfreezeAndLaunch
            && AndroidAppLaunchResult.TryRead(data, out var launchResult))
            AndroidWorkLaunchAcknowledgement.SendResult(this, Intent, launchResult.ToOperationResult());
        if (_hasAuthenticatedCommand)
        {
            data ??= new Intent();
            data.SetAction(AgnosiaActions.CommandResult);
            data.PutExtra(
                AndroidCommandContract.ExtraCommandCorrelationId,
                _commandCorrelationId.ToString("D"));
            data.PutExtra(AndroidCommandContract.ExtraCommandKind, _commandKind.ToString());
            data.PutExtra(AndroidCommandContract.ResultCommandResultCode, (int)resultCode);
            TrySignResult(data);
            Receivers.ActivityCommandResultReceiver.Send(this, Intent, resultCode, data);
        }

        var completionMessage =
            $"Finishing action={Intent?.Action ?? "<none>"}, correlationId={_commandCorrelationId}, result={resultCode}, hasData={data is not null}.";
        if (_commandKind is AndroidCommandKind.InstallPackage or AndroidCommandKind.UninstallPackage)
            Log.Info(LogTag, completionMessage);
        else
            Log.Debug(LogTag, completionMessage);
        if (data is null)
            SetResult(resultCode);
        else
            SetResult(resultCode, data);

        Finish();
    }

    private bool TryCaptureAuthenticatedCommand(string action)
    {
        if (!AndroidCommandIntentMapper.TryFromAction(action, out var actionKind)
            || !Guid.TryParse(
                Intent?.GetStringExtra(AndroidCommandContract.ExtraCommandCorrelationId),
                out var correlationId)
            || !Enum.TryParse<AndroidCommandKind>(
                Intent?.GetStringExtra(AndroidCommandContract.ExtraCommandKind),
                out var declaredKind)
            || declaredKind != actionKind)
            return false;

        _commandCorrelationId = correlationId;
        _commandKind = declaredKind;
        _hasAuthenticatedCommand = true;
        return true;
    }

    internal void HandlePackageInstallerCallback(Intent? intent)
    {
        if (_finishRequested || _packageInstallerOperationId is null
            || !string.Equals(_packageInstallerOperationId,
                intent?.GetStringExtra(AndroidCommandContract.ExtraPackageInstallerOperationId),
                StringComparison.Ordinal))
            return;

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
        var sessionId = intent?.GetIntExtra(PackageInstaller.ExtraSessionId, -1);

        Log.Info(LogTag,
            $"PackageInstaller callback status={status}, sessionId={sessionId}, operation={operation ?? "<unknown>"}, package={callbackPackage ?? "<unknown>"}, statusMessage={statusMessage ?? "<none>"}.");

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
                    RestoreHiddenStateAfterFailedPackageRemoval(
                        intent,
                        "package removal confirmation did not open");
                    FinishWithError("Android не смог открыть подтверждение установки пакета.");
                }

                return;
            }

            RestoreHiddenStateAfterFailedPackageRemoval(
                intent,
                "package removal confirmation intent missing");
            FinishWithError("Android запросил подтверждение установки, но не предоставил экран подтверждения.");
            return;
        }

        if (status == PackageInstallStatus.Success)
        {
            if (string.Equals(operation, AndroidCommandContract.PackageInstallerOperationInstall,
                    StringComparison.Ordinal))
            {
                await CompleteSuccessfulInstallAsync(callbackPackage, cancellationToken).ConfigureAwait(false);
                return;
            }

            FinishWithResult(Result.Ok);
            return;
        }

        RestoreHiddenStateAfterFailedPackageRemoval(intent, "package removal failed");
        FinishWithError(string.IsNullOrWhiteSpace(statusMessage)
            ? "Android отклонил установку пакета."
            : $"Android отклонил установку пакета: {statusMessage}");
    }

    private async Task CompleteSuccessfulInstallAsync(string? packageName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageName)
            || !await WaitForPackageAvailableAsync(packageName, cancellationToken, includeHidden: true).ConfigureAwait(false))
        {
            LogInstallPackageState(packageName, "success_but_unavailable");
            FinishWithError($"Android сообщил об успешной установке {packageName}, но пакет недоступен в рабочем профиле.");
            return;
        }
        FinishWithResult(Result.Ok);
    }

}
