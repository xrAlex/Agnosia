using Agnosia.Models;
using Android.Content;
using Android.OS;
using Java.Lang;

using Exception = System.Exception;
using OperationCanceledException = System.OperationCanceledException;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Gateways;

internal sealed class AndroidActivityCommandGateway(Func<IAndroidActivityHost> getActivityHost)
{
    private const string ActivityResultLogTag = "AgnosiaActivityResult";
    private static readonly TimeSpan DefaultExternalActivityResultTimeout = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan ProvisioningActivityResultTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultProfileCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallPackageProfileCommandTimeout = TimeSpan.FromMinutes(3);

    public Activity CurrentActivity => getActivityHost().CurrentActivity;

    public async Task<OperationResult> RunPackageOperationAsync(
        Intent intent,
        bool useWorkProfile,
        CancellationToken cancellationToken,
        string successMessage)
    {
        var result = await StartActivityForResultAsync(intent, useWorkProfile, cancellationToken)
            .ConfigureAwait(false);
        return AndroidActivityResultApi.ToPackageOperationResult(result, successMessage);
    }

    public async Task<OperationResult> RunVoidOperationAsync(
        Intent intent,
        bool useWorkProfile,
        CancellationToken cancellationToken,
        string successMessage)
    {
        var result = await StartActivityForResultAsync(intent, useWorkProfile, cancellationToken)
            .ConfigureAwait(false);
        return AndroidActivityResultApi.ToVoidOperationResult(result, successMessage);
    }

    public Task<bool> CanReachWorkProfileAsync(CancellationToken cancellationToken)
    {
        return AndroidProfileCommandGateway.CanReachWorkProfileAsync(this, cancellationToken);
    }

    public PendingIntent CreateWorkAppFrozenCallbackPendingIntent(string packageName)
    {
        var host = getActivityHost();
        return AgnosiaPendingIntentFactory.CreateWorkAppFrozenBroadcastPendingIntent(
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
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var activityResultTimeout = timeout ?? DefaultExternalActivityResultTimeout;
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(activityResultTimeout);

        try
        {
            var host = getActivityHost();
            AgnosiaRuntime.Initialize(host.CurrentActivity);
            Log.Debug(
                ActivityResultLogTag,
                $"Starting external activity for result. action={GetActionForLog(intent)}, timeoutMs={activityResultTimeout.TotalMilliseconds:0}.");
            return await host.StartForResultAsync(intent, timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Log.Warn(
                ActivityResultLogTag,
                $"Timed out waiting for external activity result. action={GetActionForLog(intent)}, timeoutMs={activityResultTimeout.TotalMilliseconds:0}.");
            return AndroidActivityResultApi.CreateCanceledResult("Системный экран не вернул результат вовремя.");
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
        if (!AndroidCommandIntentMapper.TryFromAction(intent.Action, out var kind))
            return AndroidActivityResultApi.CreateCanceledResult(
                "Android не распознал внутреннюю команду Agnosia.");

        return await StartActivityForResultAsync(
                intent,
                useWorkProfile,
                Guid.NewGuid(),
                kind,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<AndroidActivityResult> StartActivityForResultAsync(
        Intent intent,
        bool useWorkProfile,
        Guid correlationId,
        AndroidCommandKind kind,
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
                var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(activity);
                if (crossProfileApps is null || !crossProfileApps.CanInteractAcrossProfiles())
                    return AndroidActivityResultApi.CreateCanceledResult(
                        "Agnosia не разрешено напрямую обращаться к рабочему профилю.");

                var targetUser = crossProfileApps.TargetUserProfiles
                    .OfType<UserHandle>()
                    .FirstOrDefault();
                if (targetUser is null)
                    return AndroidActivityResultApi.CreateCanceledResult(
                        "Android не нашёл доступный рабочий профиль Agnosia.");

                intent.SetComponent(new ComponentName(activity, Class.FromType(host.CommandActivityType)));
                PrepareAuthenticatedCommand(intent, correlationId, kind);
                var result = await RunWorkProfileActivityCommandAsync(
                        host,
                        intent,
                        targetUser,
                        isLaunchCommand,
                        cancellationToken)
                    .ConfigureAwait(false);
                return ValidateAuthenticatedResult(result, correlationId, kind);
            }

            intent.SetComponent(new ComponentName(activity, Class.FromType(host.CommandActivityType)));
            PrepareAuthenticatedCommand(intent, correlationId, kind);
            var localResult = await RunLocalActivityCommandAsync(host, intent, cancellationToken)
                .ConfigureAwait(false);
            return ValidateAuthenticatedResult(localResult, correlationId, kind);
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

    private static async Task<AndroidActivityResult> RunLocalActivityCommandAsync(
        IAndroidActivityHost host,
        Intent intent,
        CancellationToken cancellationToken)
    {
        Log.Debug(
            ActivityResultLogTag,
            $"Starting local activity command. action={GetActionForLog(intent)}.");
        var result = await host.StartForResultAsync(intent, cancellationToken).ConfigureAwait(false);
        Log.Debug(
            ActivityResultLogTag,
            FormatActivityCommandCompleted("Local", intent, result));
        return result;
    }

    private static async Task<AndroidActivityResult> RunWorkProfileActivityCommandAsync(
        IAndroidActivityHost host,
        Intent intent,
        UserHandle targetUser,
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
            var result = await host.StartCrossProfileForResultAsync(
                    intent,
                    targetUser,
                    timeoutCancellation.Token)
                .ConfigureAwait(false);
            Log.Debug(
                ActivityResultLogTag,
                FormatActivityCommandCompleted("Work-profile", intent, result));
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
        AndroidActivityResult result)
    {
        return $"{commandScope} activity command completed. action={GetActionForLog(intent)}, result={result.ResultCode}, error={AndroidActivityResultApi.ExtractError(result) ?? "<none>"}, message={AndroidActivityResultApi.ExtractMessage(result) ?? "<none>"}.";
    }

    private static string GetActionForLog(Intent intent)
    {
        return intent.Action ?? "<none>";
    }

    private static void PrepareAuthenticatedCommand(
        Intent intent,
        Guid correlationId,
        AndroidCommandKind kind)
    {
        intent.PutExtra(AndroidCommandContract.ExtraCommandCorrelationId, correlationId.ToString("D"));
        intent.PutExtra(AndroidCommandContract.ExtraCommandKind, kind.ToString());
        AuthenticationUtility.SignIntent(intent);
    }

    private static AndroidActivityResult ValidateAuthenticatedResult(
        AndroidActivityResult result,
        Guid correlationId,
        AndroidCommandKind kind)
    {
        var data = result.Data;
        if (data is null
            || !string.Equals(data.Action, AgnosiaActions.CommandResult, StringComparison.Ordinal)
            || !AuthenticationUtility.CheckIntent(data))
            return AndroidActivityResultApi.CreateCanceledResult(
                "Рабочий профиль не вернул подписанный результат Agnosia.");

        var identity = ActivityCommandResultIdentity.Validate(
            correlationId,
            kind,
            (int)result.ResultCode,
            data.GetStringExtra(AndroidCommandContract.ExtraCommandCorrelationId),
            data.GetStringExtra(AndroidCommandContract.ExtraCommandKind),
            data.GetIntExtra(AndroidCommandContract.ResultCommandResultCode, int.MinValue));
        return identity.Succeeded
            ? result
            : AndroidActivityResultApi.CreateCanceledResult(
                "Рабочий профиль вернул результат другой команды Agnosia.");
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
