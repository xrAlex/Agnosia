using Agnosia.Models;
using Agnosia.Platform;
using Android.App.Admin;
using Android.Content;
using Android.Provider;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

public sealed class AndroidPlatformBridge : IPlatformBridge
{
    private const string LogTag = "AgnosiaPlatformBridge";
    private const int ProvisioningWarmupAttempts = 20;
    private const int ProvisioningWarmupDelayMilliseconds = 300;

    private readonly AndroidActivityCommandGateway _commandRunner;
    private readonly AndroidDashboardReader _dashboardReader;
    private readonly AndroidPermissionCoordinator _permissionCoordinator;
    private readonly AndroidAppCommandCoordinator _appCommandCoordinator;
    private readonly Lock _provisioningReadinessPollingSync = new();
    private WeakReference<IAndroidActivityHost>? _activityHostReference;
    private CancellationTokenSource? _provisioningReadinessPollingCancellation;

    public static AndroidPlatformBridge Instance { get; } = new();

    private AndroidPlatformBridge()
    {
        _commandRunner = new AndroidActivityCommandGateway(GetActivityHost);
        _dashboardReader = new AndroidDashboardReader(_commandRunner);
        _permissionCoordinator = new AndroidPermissionCoordinator(_commandRunner, StartProvisioningAsync);
        _appCommandCoordinator = new AndroidAppCommandCoordinator(
            _commandRunner,
            _permissionCoordinator,
            _dashboardReader.LoadDashboardAsync);
    }

    public void AttachActivity(IAndroidActivityHost activityHost)
    {
        AgnosiaRuntime.Initialize(activityHost.CurrentActivity);
        _activityHostReference = new WeakReference<IAndroidActivityHost>(activityHost);
        TryStartPendingProvisioningReadinessPolling("activity_attached");
    }

    public void DetachActivity()
    {
        _activityHostReference = null;
        CancelPendingProvisioningReadinessPolling();
    }

    public Task<DashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken = default) =>
        _dashboardReader.LoadDashboardAsync(cancellationToken);

    public Task<IReadOnlyList<AppLogEntry>> LoadRecentLogsAsync(CancellationToken cancellationToken = default) =>
        _dashboardReader.LoadRecentLogsAsync(cancellationToken);

    public Task<IReadOnlyList<PermissionSnapshot>> LoadPermissionsAsync(CancellationToken cancellationToken = default) =>
        _permissionCoordinator.LoadPermissionsAsync(cancellationToken);

    public Task<OperationResult> RequestPermissionAsync(PermissionKind permission, CancellationToken cancellationToken = default) =>
        _permissionCoordinator.RequestPermissionAsync(permission, cancellationToken);

    public Task<OperationResult> OpenAppDetailsSettingsAsync(CancellationToken cancellationToken = default)
    {
        var result = _permissionCoordinator.OpenAppDetailsSettings();
        return Task.FromResult(result);
    }

    public async Task<bool> LoadOnboardingCompletedAsync(CancellationToken cancellationToken = default)
    {
        var activity = GetActivityHost().CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return await Task.FromResult(LocalStorageManager.Instance.GetBoolean(StorageKeys.OnboardingCompleted));
    }

    public Task<OperationResult> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        AgnosiaRuntime.Initialize(GetActivityHost().CurrentActivity);
        LocalStorageManager.Instance.SetBoolean(StorageKeys.OnboardingCompleted, true);
        return Task.FromResult(OperationResult.Success("Первичная настройка завершена."));
    }

    public async Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        var host = GetActivityHost();
        var activity = host.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (AndroidSystemApi.GetDevicePolicyManager(activity) is not { } policyManager)
            return OperationResult.Failure("На этом устройстве недоступны API политики устройства.");
        

        if (!AndroidProvisioningApi.CanStartManagedProfileProvisioning(policyManager))
            return OperationResult.Failure("Android сообщает, что создание рабочего профиля сейчас недоступно.");
        
        AuthenticationUtility.Reset();
        AgnosiaUtilities.MarkWorkProfileSetupStarted();
        var authKey = AuthenticationUtility.CreateAndStoreKey();

        var intent = new Intent(DevicePolicyManager.ActionProvisionManagedProfile);
        AndroidProvisioningApi.ConfigureManagedProfileProvisioningIntent(
            intent,
            AgnosiaUtilities.GetAdminComponent(activity, host.AdminReceiverType),
            authKey);

        var result = await _commandRunner.StartExternalActivityForResultAsync(intent, cancellationToken);
        if (result.ResultCode != Result.Ok)
        {
            if (AgnosiaUtilities.HasAssociatedProfile(activity))
            {
                TryStartPendingProvisioningReadinessPolling("provisioning_result_with_associated_profile");
                return OperationResult.Success("Рабочий профиль создан, но Android не вернул код успешного завершения. Проверяем доступность профиля.");
            }

            AgnosiaUtilities.ClearWorkProfileConfiguredState();
            AuthenticationUtility.Reset();
            return OperationResult.Failure("Создание рабочего профиля отменено или отклонено Android.");
        }

        if (await WaitForWorkProfileAvailabilityAsync(cancellationToken))
        {
            AgnosiaUtilities.MarkWorkProfileReady();
            return OperationResult.Success("Рабочий профиль подключен.");
        }

        if (AgnosiaUtilities.HasAssociatedProfile(activity))
        {
            TryStartPendingProvisioningReadinessPolling("provisioning_result_pending_work_profile");
            return OperationResult.Success("Рабочий профиль создан. Android еще завершает запуск, обновите состояние через несколько секунд.");
        }

        return OperationResult.Success("Создание рабочего профиля запущено. Завершите шаги Android и вернитесь в Agnosia.");
    }

    public async Task<OperationResult> OpenWorkProfileSettingsAsync(CancellationToken cancellationToken = default)
    {
        var activity = GetActivityHost().CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var intent = new Intent(Settings.ActionSyncSettings);
        var result = await _commandRunner.StartExternalActivityForResultAsync(intent, cancellationToken);
        if (result.ResultCode == Result.Canceled
            && !string.IsNullOrWhiteSpace(AndroidActivityResultApi.ExtractError(result)))
        {
            var fallbackIntent = new Intent(Settings.ActionSettings);
            var fallbackResult = await _commandRunner.StartExternalActivityForResultAsync(fallbackIntent, cancellationToken);
            if (fallbackResult.ResultCode == Result.Canceled
                && !string.IsNullOrWhiteSpace(AndroidActivityResultApi.ExtractError(fallbackResult)))
            {
                return OperationResult.Failure("Android не смог открыть настройки устройства.");
            }
        }

        return OperationResult.Success("Проверяем состояние рабочего профиля после возврата из настроек.");
    }

    public Task<OperationResult> CloneAsync(AppSnapshot app, CancellationToken cancellationToken = default) =>
        _appCommandCoordinator.CloneAsync(app, cancellationToken);

    public Task<OperationResult> UninstallAsync(AppSnapshot app, CancellationToken cancellationToken = default) =>
        _appCommandCoordinator.UninstallAsync(app, cancellationToken);

    public Task<OperationResult> SetFrozenAsync(AppSnapshot app, bool hidden, CancellationToken cancellationToken = default) =>
        _appCommandCoordinator.SetFrozenAsync(app, hidden, cancellationToken);

    public Task<OperationResult> ForceFreezeAsync(AppSnapshot app, CancellationToken cancellationToken = default) =>
        _appCommandCoordinator.ForceFreezeAsync(app, cancellationToken);

    public Task<OperationResult> CreateShortcutAsync(AppSnapshot app, CancellationToken cancellationToken = default) =>
        _appCommandCoordinator.CreateShortcutAsync(app, cancellationToken);

    public Task<OperationResult> LaunchAsync(AppSnapshot app, CancellationToken cancellationToken = default) =>
        _appCommandCoordinator.LaunchAsync(app, cancellationToken);

    public Task<OperationResult> SetInteractionAccessAsync(AppSnapshot app, bool enabled, CancellationToken cancellationToken = default) =>
        _appCommandCoordinator.SetInteractionAccessAsync(app, enabled, cancellationToken);

    public Task<OperationResult> SaveSettingsAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        var activity = GetActivityHost().CurrentActivity;
        return AndroidSettingsStore.SaveAsync(activity, settings, cancellationToken);
    }

    public void NotifyManagedProfileProvisioned(Context context, Intent? intent)
    {
        AgnosiaRuntime.Initialize(context);
        AgnosiaUtilities.MarkManagedProfileProvisioned(context, intent);
        TryStartPendingProvisioningReadinessPolling("managed_profile_provisioned_broadcast");
    }

    private async Task<bool> WaitForWorkProfileAvailabilityAsync(CancellationToken cancellationToken)
    {
        var activity = GetActivityHost().CurrentActivity;
        for (var attempt = 0; attempt < ProvisioningWarmupAttempts; attempt++)
        {
            if (AgnosiaUtilities.HasWorkProfileTarget(activity) && await _commandRunner.CanReachWorkProfileAsync(cancellationToken))
            {
                return true;
            }

            if (attempt < ProvisioningWarmupAttempts - 1)
            {
                await Task.Delay(ProvisioningWarmupDelayMilliseconds, cancellationToken);
            }
        }

        return false;
    }

    private void TryStartPendingProvisioningReadinessPolling(string trigger)
    {
        if (!ShouldPollForProvisioningReadiness())
        {
            return;
        }

        if (!TryGetActivityHost(out _))
        {
            Log.Info(LogTag, $"Deferred work-profile readiness polling until the primary activity is attached. trigger={trigger}.");
            return;
        }

        CancellationTokenSource pollingCancellation;
        lock (_provisioningReadinessPollingSync)
        {
            if (_provisioningReadinessPollingCancellation is { IsCancellationRequested: false })
            {
                return;
            }

            pollingCancellation = new CancellationTokenSource();
            _provisioningReadinessPollingCancellation = pollingCancellation;
        }

        _ = PollForWorkProfileReadinessAsync(trigger, pollingCancellation);
    }

    private static bool ShouldPollForProvisioningReadiness()
    {
        var storage = LocalStorageManager.Instance;
        return !storage.GetBoolean(StorageKeys.HasSetup)
            && (storage.GetBoolean(StorageKeys.IsSettingUp)
                || storage.GetLong(StorageKeys.ManagedProfileProvisionedAtUtc) > 0);
    }

    private async Task PollForWorkProfileReadinessAsync(
        string trigger,
        CancellationTokenSource pollingCancellation)
    {
        try
        {
            Log.Info(LogTag, $"Polling work-profile readiness. trigger={trigger}.");
            if (await WaitForWorkProfileAvailabilityAsync(pollingCancellation.Token))
            {
                AgnosiaUtilities.MarkWorkProfileReady();
                Log.Info(LogTag, "Work-profile Agnosia confirmed profile-owner readiness.");
                return;
            }

            Log.Warn(LogTag, "Work-profile readiness polling finished without profile-owner confirmation.");
        }
        catch (OperationCanceledException) when (pollingCancellation.IsCancellationRequested)
        {
            Log.Debug(LogTag, "Work-profile readiness polling canceled.");
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Work-profile readiness polling failed: {exception}");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Unexpected work-profile readiness polling failure: {exception}");
        }
        finally
        {
            ClearProvisioningReadinessPolling(pollingCancellation);
        }
    }

    private void CancelPendingProvisioningReadinessPolling()
    {
        lock (_provisioningReadinessPollingSync)
        {
            _provisioningReadinessPollingCancellation?.Cancel();
        }
    }

    private void ClearProvisioningReadinessPolling(CancellationTokenSource pollingCancellation)
    {
        lock (_provisioningReadinessPollingSync)
        {
            if (ReferenceEquals(_provisioningReadinessPollingCancellation, pollingCancellation))
            {
                _provisioningReadinessPollingCancellation = null;
            }
        }

        pollingCancellation.Dispose();
    }

    private bool TryGetActivityHost(out IAndroidActivityHost activityHost)
    {
        if (_activityHostReference?.TryGetTarget(out var target) == true)
        {
            activityHost = target;
            return true;
        }

        activityHost = null!;
        return false;
    }

    private IAndroidActivityHost GetActivityHost()
    {
        return TryGetActivityHost(out var activityHost)
            ? activityHost
            : throw new InvalidOperationException("Agnosia is not attached to an active Android activity.");
    }

}
