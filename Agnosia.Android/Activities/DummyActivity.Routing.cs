using Agnosia.Android.Infrastructure;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private void HandleAction()
    {
        var action = Intent?.Action;
        if (string.IsNullOrWhiteSpace(action))
        {
            Finish();
            return;
        }

        if (string.Equals(action, AgnosiaActions.PackageInstallerCallback, StringComparison.Ordinal))
        {
            HandlePackageInstallerCallback(Intent);
            return;
        }

        Log.Debug(LogTag, $"Handling signed action={action}, isProfileOwner={_isProfileOwner}.");
        if (string.Equals(action, AgnosiaActions.RecoverAuthentication, StringComparison.Ordinal))
        {
            ActionRecoverAuthentication();
            return;
        }

        if (!AuthenticationUtility.CheckIntent(Intent)
            && !AuthenticationUtility.CheckWorkAppFrozenCallback(Intent))
        {
            Log.Warn(LogTag,
                $"Rejected signed action={action}: authentication check failed. isProfileOwner={_isProfileOwner}.");
            Finish();
            return;
        }

        try
        {
            switch (action)
            {
                case AgnosiaActions.ProfilePing:
                    FinishWithProfileOwnerCheck();
                    break;
                case AgnosiaActions.QueryApps:
                    RunCommandCenterAction(AndroidCommandKind.QueryApps, "Android не смог получить список приложений.");
                    break;
                case AgnosiaActions.QueryAppIcon:
                    RunAction(ActionQueryAppIconAsync, "Android не смог получить иконку приложения.");
                    break;
                case AgnosiaActions.QueryAppIcons:
                    RunCommandCenterAction(AndroidCommandKind.QueryAppIcons, "Android не смог получить иконки приложений.");
                    break;
                case AgnosiaActions.QueryLogs:
                    RunCommandCenterAction(AndroidCommandKind.QueryLogs, "Android не смог получить журнал.");
                    break;
                case AgnosiaActions.QueryCrossProfilePackages:
                    RunCommandCenterAction(AndroidCommandKind.QueryCrossProfilePackages, "Android не смог получить список межпрофильных пакетов.");
                    break;
                case AgnosiaActions.QueryPermissions:
                    RunCommandCenterAction(AndroidCommandKind.QueryPermissions, "Android не смог получить разрешения.");
                    break;
                case AgnosiaActions.QueryUsageStatsAccess:
                    ActionQueryUsageStatsAccess();
                    break;
                case AgnosiaActions.RequestUsageStatsAccess:
                    ActionRequestUsageStatsAccess();
                    break;
                case AgnosiaActions.QueryPackageInstallAccess:
                    ActionQueryPackageInstallAccess();
                    break;
                case AgnosiaActions.RequestPackageInstallAccess:
                    ActionRequestPackageInstallAccess();
                    break;
                case AgnosiaActions.QueryAllFilesAccess:
                    ActionQueryAllFilesAccess();
                    break;
                case AgnosiaActions.RequestAllFilesAccess:
                    ActionRequestAllFilesAccess();
                    break;
                case AgnosiaActions.InstallPackage:
                    ActionInstallPackage();
                    break;
                case AgnosiaActions.UninstallPackage:
                    ActionUninstallPackage();
                    break;
                case AgnosiaActions.PrepareHiddenShortcut:
                    RunAction(ActionPrepareHiddenShortcutAsync,
                        "Android не смог подготовить ярлык скрытого приложения.");
                    break;
                case AgnosiaActions.CreateHiddenShortcut:
                    ActionCreateHiddenShortcut();
                    break;
                case AgnosiaActions.FreezePackage:
                    ActionFreezePackage(true);
                    break;
                case AgnosiaActions.UnfreezePackage:
                    ActionFreezePackage(false);
                    break;
                case AgnosiaActions.RevokeRuntimePermissions:
                    RunAction(ActionRevokeRuntimePermissionsAsync,
                        "Android не смог отозвать runtime-разрешения.");
                    break;
                case AgnosiaActions.SetLockdownEnabled:
                    ActionSetLockdownEnabled();
                    break;
                case AgnosiaActions.SetLockdownInternetAccess:
                    ActionSetLockdownInternetAccess();
                    break;
                case AgnosiaActions.UnfreezeAndLaunch:
                    ActionUnfreezeAndLaunch();
                    break;
                case AgnosiaActions.SetCrossProfileInteraction:
                    ActionSetCrossProfileInteraction();
                    break;
                case AgnosiaActions.StartFileShuttleParentToWork:
                case AgnosiaActions.StartFileShuttleWorkToParent:
                    ActionStartFileShuttle();
                    break;
                case AgnosiaActions.SynchronizePreference:
                    ActionSynchronizePreference();
                    break;
                case AgnosiaActions.WorkAppFrozen:
                    RunAction(ActionWorkAppFrozenAsync, "Android не смог обработать событие заморозки приложения.");
                    break;
                case AgnosiaActions.FinalizeProvision:
                    ActionFinalizeProvision();
                    break;
                default:
                    Finish();
                    break;
            }
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to handle action {action}: {exception}");
            FinishWithError("Android не смог выполнить системное действие Agnosia.");
        }
    }

    private void ActionRecoverAuthentication()
    {
        if (!_isProfileOwner)
        {
            FinishWithProfileOwnerCheck();
            return;
        }

        var replacementAuthKey = Intent?.GetStringExtra(AndroidCommandContract.ExtraReplacementAuthKey);
        if (!AuthenticationUtility.TryStoreProvisioningKey(replacementAuthKey))
        {
            FinishWithError("Android не смог восстановить ключ управления рабочим профилем.");
            return;
        }

        FinishWithProfileOwnerCheck();
    }

    private void FinishWithProfileOwnerCheck()
    {
        var pingResult = new Intent();
        pingResult.PutExtra(AndroidCommandContract.ResultProfileOwnerCheckPerformed, true);
        pingResult.PutExtra(AndroidCommandContract.ResultIsProfileOwner, _isProfileOwner);
        WriteAppVersionToResult(pingResult);
        if (!_isProfileOwner)
        {
            pingResult.PutExtra(AndroidCommandContract.ResultError,
                "Рабочий профиль не управляется Agnosia.");
            TrySignResult(pingResult);
            FinishWithResult(Result.Canceled, pingResult);
            return;
        }

        AndroidStartup.EnsureWorkProfilePoliciesAndStartLockFreezeMonitor(this);
        AuthenticationUtility.SignIntent(pingResult);
        FinishWithResult(Result.Ok, pingResult);
    }

    private void WriteAppVersionToResult(Intent result)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PackageName)) return;

            var packageInfo = PackageManager?.GetPackageInfo(PackageName, PackageInfoFlags.MatchAll);
            if (packageInfo is null) return;

            result.PutExtra(AndroidCommandContract.ResultAppVersionCode, packageInfo.LongVersionCode);
            result.PutExtra(AndroidCommandContract.ResultAppVersionName, packageInfo.VersionName);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to write app version to profile check result: {exception.Message}");
        }
    }

    private static void TrySignResult(Intent result)
    {
        if (string.IsNullOrWhiteSpace(AuthenticationUtility.GetExistingKey())) return;

        AuthenticationUtility.SignIntent(result);
    }

    private void RunAction(
        Func<CancellationToken, Task> action,
        string fallbackErrorMessage)
    {
        var cancellationToken = _destroyCancellation.Token;
        _ = RunActionAsync(action, fallbackErrorMessage, cancellationToken);
    }

    private async Task RunActionAsync(
        Func<CancellationToken, Task> action,
        string fallbackErrorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"{fallbackErrorMessage} Details: {exception}");
            FinishWithError(fallbackErrorMessage);
        }
    }

    private void RunCommandCenterAction(AndroidCommandKind kind, string fallbackErrorMessage)
    {
        var payloadJson = CreateCommandRequestPayloadJson(kind, Intent);
        var targetProfile = _isProfileOwner
            ? AndroidCommandTargetProfile.Work
            : AndroidCommandTargetProfile.Personal;
        var envelope = new AndroidCommandEnvelope(
            Guid.NewGuid(),
            kind,
            targetProfile,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh,
            TimeSpan.FromSeconds(30),
            payloadJson);

        RunAction(
            async cancellationToken =>
            {
                var contextFactory = ServiceRegistry.GetRequiredService<AndroidCommandExecutionContextFactory>();
                var executor = ServiceRegistry.GetRequiredService<AndroidCommandHandlerExecutor>();
                var context = contextFactory.Create(
                    this,
                    this,
                    envelope,
                    AndroidCommandTransportKind.Activity,
                    "dummy-activity");
                var result = await executor.ExecuteAsync(envelope, context, cancellationToken)
                    .ConfigureAwait(false);

                if (!result.Succeeded)
                {
                    FinishWithError(string.IsNullOrWhiteSpace(result.Message)
                        ? fallbackErrorMessage
                        : result.Message);
                    return;
                }

                FinishWithResult(Result.Ok, CreateCommandResultIntent(
                    kind,
                    result.PayloadJson,
                    result.Diagnostics,
                    Intent));
            },
            fallbackErrorMessage);
    }
}
