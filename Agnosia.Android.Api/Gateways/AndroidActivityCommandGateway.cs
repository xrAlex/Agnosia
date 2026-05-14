using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Dashboard;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Platform;
using Agnosia.Models;
using Android.Content;
using Java.Lang;
using Stopwatch = System.Diagnostics.Stopwatch;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Gateways;

internal sealed class AndroidActivityCommandGateway(Func<IAndroidActivityHost> getActivityHost)
{
    private const string ActivityResultLogTag = "AgnosiaActivityResult";
    private static readonly TimeSpan DefaultProfileCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallPackageProfileCommandTimeout = TimeSpan.FromMinutes(3);

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

    public Task<bool> CanReachWorkProfileAsync(CancellationToken cancellationToken)
    {
        return AndroidProfileCommandGateway.CanReachWorkProfileAsync(this, cancellationToken);
    }

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
            var isLaunchCommand = IsLaunchCommand(intent);

            if (useWorkProfile
                && isLaunchCommand
                && TryCreatePreflightLaunchFailure(activity, intent) is { } preflightFailure)
                return preflightFailure;

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
                Log.Debug(ActivityResultLogTag,
                    $"Starting local activity command. action={intent.Action ?? "<none>"}.");
                var localStartedAt = Stopwatch.GetTimestamp();
                var localResult = await host.StartForResultAsync(intent, cancellationToken);
                Log.Debug(
                    ActivityResultLogTag,
                    $"Local activity command completed. action={intent.Action ?? "<none>"}, result={localResult.ResultCode}, elapsedMs={Stopwatch.GetElapsedTime(localStartedAt).TotalMilliseconds:0}, error={AndroidActivityResultApi.ExtractError(localResult) ?? "<none>"}, message={AndroidActivityResultApi.ExtractMessage(localResult) ?? "<none>"}.");
                return localResult;
            }

            var profileCommandTimeout = GetProfileCommandTimeout(intent);
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(profileCommandTimeout);
            try
            {
                Log.Debug(
                    ActivityResultLogTag,
                    $"Starting work-profile activity command. action={intent.Action ?? "<none>"}, timeoutMs={profileCommandTimeout.TotalMilliseconds:0}.");
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
                    $"Timed out waiting for work-profile activity result. action={intent.Action ?? "<none>"}, timeoutMs={profileCommandTimeout.TotalMilliseconds:0}.");
                if (isLaunchCommand)
                {
                    var launchResult = CreateLaunchResult(intent)
                        .Fail(
                            AndroidAppLaunchStage.CommandReceived,
                            AndroidAppLaunchIssueKind.WorkProfileUnavailable,
                            $"timeoutMs={profileCommandTimeout.TotalMilliseconds:0}");
                    launchResult.Log(ActivityResultLogTag);
                    return launchResult.ToActivityResult();
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
            if (IsLaunchCommand(intent))
            {
                var issue = exception is InvalidOperationException
                    ? AndroidAppLaunchIssueKind.WorkProfileUnavailable
                    : AndroidAppLaunchResult.ClassifyStartActivityException(exception);
                var launchResult = CreateLaunchResult(intent)
                    .Fail(
                        AndroidAppLaunchStage.CommandReceived,
                        issue,
                        exception.ToString());
                launchResult.Log(ActivityResultLogTag);
                return launchResult.ToActivityResult();
            }

            var error = AndroidRecoverableException.ToUserMessage(exception);
            Log.Warn(ActivityResultLogTag, $"{error} Details: {exception}");
            return AndroidActivityResultApi.CreateCanceledResult(error);
        }
    }

    private static bool IsLaunchCommand(Intent intent)
    {
        return string.Equals(intent.Action, AgnosiaActions.UnfreezeAndLaunch, StringComparison.Ordinal);
    }

    private static TimeSpan GetProfileCommandTimeout(Intent intent)
    {
        return string.Equals(intent.Action, AgnosiaActions.InstallPackage, StringComparison.Ordinal)
            ? InstallPackageProfileCommandTimeout
            : DefaultProfileCommandTimeout;
    }

    private static AndroidAppLaunchResult CreateLaunchResult(Intent intent)
    {
        return AndroidAppLaunchResult.CommandReceived(
            intent.GetStringExtra("packageName"),
            intent.GetStringExtra("displayName"));
    }

    private static AndroidActivityResult? TryCreatePreflightLaunchFailure(Activity activity, Intent intent)
    {
        try
        {
            var diagnostics = AndroidWorkProfileDiagnosticsReader.Read(activity);
            if (diagnostics.QuietModeEnabled == true)
            {
                var launchResult = CreateLaunchResult(intent)
                    .Fail(
                        AndroidAppLaunchStage.CommandReceived,
                        AndroidAppLaunchIssueKind.QuietMode,
                        diagnostics.ToLogString());
                launchResult.Log(ActivityResultLogTag);
                return launchResult.ToActivityResult();
            }
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(ActivityResultLogTag, $"Could not read work-profile launch preflight diagnostics: {exception}");
        }

        return null;
    }
}