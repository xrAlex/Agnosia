using Agnosia.Models;
using Android.Content;
using Java.Lang;
using Stopwatch = System.Diagnostics.Stopwatch;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

internal sealed class AndroidActivityCommandGateway(Func<IAndroidActivityHost> getActivityHost)
{
    private const string ActivityResultLogTag = "AgnosiaActivityResult";
    private static readonly TimeSpan ProfileCommandTimeout = TimeSpan.FromSeconds(30);

    public Activity CurrentActivity => getActivityHost().CurrentActivity;

    public async Task<OperationResult> RunPackageOperationAsync(
        Intent intent,
        bool useWorkProfile,
        CancellationToken cancellationToken,
        string successMessage)
    {
        var result = await StartActivityForResultAsync(intent, useWorkProfile, cancellationToken);
        return AndroidActivityResultApi.ToPackageOperationResult(result, successMessage);
    }

    public async Task<OperationResult> RunVoidOperationAsync(
        Intent intent,
        bool useWorkProfile,
        CancellationToken cancellationToken,
        string successMessage)
    {
        var result = await StartActivityForResultAsync(intent, useWorkProfile, cancellationToken);
        return AndroidActivityResultApi.ToVoidOperationResult(result, successMessage);
    }

    public Task<bool> CanReachWorkProfileAsync(CancellationToken cancellationToken) =>
        AndroidProfileCommandGateway.CanReachWorkProfileAsync(this, cancellationToken);

    public PendingIntent CreateWorkAppFrozenCallbackPendingIntent(string packageName)
    {
        var host = getActivityHost();
        return AndroidPendingIntentApi.CreateWorkAppFrozenBroadcastPendingIntent(
            host.CurrentActivity,
            host.WorkAppFrozenReceiverType,
            packageName);
    }

    public async Task<AndroidActivityResult> StartExternalActivityForResultAsync(
        Intent intent,
        CancellationToken cancellationToken)
    {
        try
        {
            var host = getActivityHost();
            AgnosiaRuntime.Initialize(host.CurrentActivity);
            return await host.StartForResultAsync(intent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            var error = AndroidRecoverableException.ToUserMessage(exception);
            Log.Warn(ActivityResultLogTag, $"{error} Details: {exception}");
            return AndroidActivityResultApi.CreateCanceledResult(error);
        }
    }

    public async Task<AndroidActivityResult> StartActivityForResultAsync(
        Intent intent,
        bool useWorkProfile,
        CancellationToken cancellationToken)
    {
        try
        {
            var host = getActivityHost();
            var activity = host.CurrentActivity;
            AgnosiaRuntime.Initialize(activity);

            if (useWorkProfile)
            {
                AgnosiaUtilities.TransferIntentToProfile(activity, intent);
            }
            else
            {
                intent.SetComponent(new ComponentName(activity, Class.FromType(host.CommandActivityType)));
                AuthenticationUtility.SignIntent(intent);
            }

            if (!useWorkProfile)
            {
                Log.Debug(ActivityResultLogTag, $"Starting local activity command. action={intent.Action ?? "<none>"}.");
                var localStartedAt = Stopwatch.GetTimestamp();
                var localResult = await host.StartForResultAsync(intent, cancellationToken);
                Log.Debug(
                    ActivityResultLogTag,
                    $"Local activity command completed. action={intent.Action ?? "<none>"}, result={localResult.ResultCode}, elapsedMs={Stopwatch.GetElapsedTime(localStartedAt).TotalMilliseconds:0}, error={AndroidActivityResultApi.ExtractError(localResult) ?? "<none>"}, message={AndroidActivityResultApi.ExtractMessage(localResult) ?? "<none>"}.");
                return localResult;
            }

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(ProfileCommandTimeout);
            try
            {
                Log.Debug(
                    ActivityResultLogTag,
                    $"Starting work-profile activity command. action={intent.Action ?? "<none>"}, timeoutMs={ProfileCommandTimeout.TotalMilliseconds:0}.");
                var workStartedAt = Stopwatch.GetTimestamp();
                var workResult = await host.StartForResultAsync(intent, timeoutCancellation.Token);
                Log.Debug(
                    ActivityResultLogTag,
                    $"Work-profile activity command completed. action={intent.Action ?? "<none>"}, result={workResult.ResultCode}, elapsedMs={Stopwatch.GetElapsedTime(workStartedAt).TotalMilliseconds:0}, error={AndroidActivityResultApi.ExtractError(workResult) ?? "<none>"}, message={AndroidActivityResultApi.ExtractMessage(workResult) ?? "<none>"}.");
                return workResult;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warn(
                    ActivityResultLogTag,
                    $"Timed out waiting for work-profile activity result. action={intent.Action ?? "<none>"}, timeoutMs={ProfileCommandTimeout.TotalMilliseconds:0}.");
                if (string.Equals(intent.Action, AgnosiaActions.UnfreezeAndLaunch, StringComparison.Ordinal))
                {
                    Log.Info(
                        ActivityResultLogTag,
                        "Treating UNFREEZE_AND_LAUNCH timeout as non-fatal because the hidden-app launch is monitored by the work-profile session service.");
                    var data = new Intent();
                    data.PutExtra(AndroidCommandContract.ResultMessage, "Открываем приложение.");
                    return new AndroidActivityResult(Result.Ok, data);
                }

                return AndroidActivityResultApi.CreateCanceledResult(
                    "Рабочий профиль не ответил на системную команду вовремя.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            var error = AndroidRecoverableException.ToUserMessage(exception);
            Log.Warn(ActivityResultLogTag, $"{error} Details: {exception}");
            return AndroidActivityResultApi.CreateCanceledResult(error);
        }
    }
}
