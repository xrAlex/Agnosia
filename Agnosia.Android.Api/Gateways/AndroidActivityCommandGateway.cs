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

    public Task<OperationResult> DisconnectPreparedVpnAsync(CancellationToken cancellationToken)
    {
        var host = getActivityHost();
        AgnosiaRuntime.Initialize(host.CurrentActivity);
        return host.DisconnectPreparedVpnAsync(cancellationToken);
    }

    public void ShowVpnGuardOverlay()
    {
        getActivityHost().ShowVpnGuardOverlay();
    }

    public void HideVpnGuardOverlay()
    {
        getActivityHost().HideVpnGuardOverlay();
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
                return await RunWorkProfileActivityCommandAsync(
                    host,
                    intent,
                    isLaunchCommand,
                    cancellationToken);
            }

            intent.SetComponent(new ComponentName(activity, Class.FromType(host.CommandActivityType)));
            AuthenticationUtility.SignIntent(intent);
            return await RunLocalActivityCommandAsync(host, intent, cancellationToken);
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

    internal async Task<AndroidActivityResult> StartUnsignedWorkProfileActivityForResultAsync(
        Intent intent,
        CancellationToken cancellationToken)
    {
        try
        {
            var host = getActivityHost();
            var activity = host.CurrentActivity;
            AgnosiaRuntime.Initialize(activity);
            AgnosiaUtilities.TransferIntentToProfileWithoutAuthentication(activity, intent);
            return await RunWorkProfileActivityCommandAsync(
                host,
                intent,
                false,
                cancellationToken);
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

    private static async Task<AndroidActivityResult> RunLocalActivityCommandAsync(
        IAndroidActivityHost host,
        Intent intent,
        CancellationToken cancellationToken)
    {
        Log.Debug(
            ActivityResultLogTag,
            $"Starting local activity command. action={GetActionForLog(intent)}.");
        var startedAt = Stopwatch.GetTimestamp();
        var result = await host.StartForResultAsync(intent, cancellationToken);
        Log.Debug(
            ActivityResultLogTag,
            FormatActivityCommandCompleted("Local", intent, result, startedAt));
        return result;
    }

    private static async Task<AndroidActivityResult> RunWorkProfileActivityCommandAsync(
        IAndroidActivityHost host,
        Intent intent,
        bool isLaunchCommand,
        CancellationToken cancellationToken)
    {
        var profileCommandTimeout = GetProfileCommandTimeout(intent);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(profileCommandTimeout);
        try
        {
            Log.Debug(
                ActivityResultLogTag,
                $"Starting work-profile activity command. action={GetActionForLog(intent)}, timeoutMs={profileCommandTimeout.TotalMilliseconds:0}.");
            var startedAt = Stopwatch.GetTimestamp();
            var result = await host.StartForResultAsync(intent, timeoutCancellation.Token);
            Log.Debug(
                ActivityResultLogTag,
                FormatActivityCommandCompleted("Work-profile", intent, result, startedAt));
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn(
                ActivityResultLogTag,
                $"Timed out waiting for work-profile activity result. action={GetActionForLog(intent)}, timeoutMs={profileCommandTimeout.TotalMilliseconds:0}.");
            return CreateWorkProfileTimeoutResult(intent, isLaunchCommand, profileCommandTimeout);
        }
    }

    private static AndroidActivityResult CreateWorkProfileTimeoutResult(
        Intent intent,
        bool isLaunchCommand,
        TimeSpan profileCommandTimeout)
    {
        if (!isLaunchCommand)
            return AndroidActivityResultApi.CreateCanceledResult(
                "Рабочий профиль не ответил на системную команду вовремя.");

        var launchResult = CreateLaunchResult(intent)
            .Fail(
                AndroidAppLaunchStage.CommandReceived,
                AndroidAppLaunchIssueKind.WorkProfileUnavailable,
                $"timeoutMs={profileCommandTimeout.TotalMilliseconds:0}");
        launchResult.Log(ActivityResultLogTag);
        return launchResult.ToActivityResult();
    }

    private static string FormatActivityCommandCompleted(
        string commandScope,
        Intent intent,
        AndroidActivityResult result,
        long startedAt)
    {
        return $"{commandScope} activity command completed. action={GetActionForLog(intent)}, result={result.ResultCode}, elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}, error={AndroidActivityResultApi.ExtractError(result) ?? "<none>"}, message={AndroidActivityResultApi.ExtractMessage(result) ?? "<none>"}.";
    }

    private static string GetActionForLog(Intent intent)
    {
        return intent.Action ?? "<none>";
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
            intent.GetStringExtra(AndroidCommandContract.ExtraLaunchPackageName),
            intent.GetStringExtra(AndroidCommandContract.ExtraLaunchDisplayName));
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
