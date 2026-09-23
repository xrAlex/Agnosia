using Agnosia.Models;
using Android.App.Admin;
using Android.Content;
using Android.Provider;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Platform;

internal sealed class AndroidProvisioningCoordinator(
    AndroidActivityCommandGateway commandRunner,
    Func<IAndroidActivityHost> getActivityHost)
{
    private const string LogTag = "AgnosiaPlatformBridge";
    private const string ManagedProfileSettingsAction = "android.settings.MANAGED_PROFILE_SETTINGS";
    private const int ProvisioningWarmupAttempts = 5;
    private const int ProvisioningWarmupDelayMilliseconds = 2000;
    private static readonly SemaphoreSlim ProvisioningLock = new(1, 1);

    internal static bool IsProvisioningInProgress => ProvisioningLock.CurrentCount == 0;

    public Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        return StartProvisioningAsync(false, cancellationToken);
    }

    public Task<OperationResult> StartOfflineProvisioningAsync(CancellationToken cancellationToken = default)
    {
        return StartProvisioningAsync(true, cancellationToken);
    }

    public async Task<OperationResult> StartDirectProvisioningAsync(CancellationToken cancellationToken = default)
    {
        if (!await ProvisioningLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return OperationResult.Failure("Создание рабочего профиля уже выполняется.");
        AndroidDirectProfileProvisioningStore? store = null;
        try
        {
            var activity = GetInitializedActivity();
            var packageName = activity.PackageName;
            if (string.IsNullOrWhiteSpace(packageName) || AndroidSystemApi.GetUserManager(activity)?.IsManagedProfile != false)
                return OperationResult.Failure("Создание через root нужно запускать из личного профиля.");
            store = new AndroidDirectProfileProvisioningStore(ServiceRegistry.GetRequiredService<LocalStorageManager>(),
                Settings.Global.GetInt(activity.ContentResolver, Settings.Global.BootCount, -1));
            var admin = AgnosiaUtilities.GetAdminComponent(activity, getActivityHost().AdminReceiverType).FlattenToString()
                        ?? throw new InvalidOperationException("Missing device admin component.");
            AndroidQueryCache.Shared.ClearOwnerCheck();
            var workflow = new DirectProfileProvisioningWorkflow(new AndroidRootCommandRunner(), store,
                packageName, admin, async (_, token) =>
                {
                    AndroidQueryCache.Shared.ClearOwnerCheck();
                    return await WaitForWorkProfileAvailabilityAsync(token, attempts: 10).ConfigureAwait(false);
                });
            var result = await workflow.RunAsync(global::Android.OS.Process.MyUserHandle()?.GetHashCode() ?? -1, cancellationToken)
                .ConfigureAwait(false);
            if (result.Succeeded) AgnosiaUtilities.MarkWorkProfileReady();
            else if (store.DidWrite) AgnosiaUtilities.MarkWorkProfileResetRequired();
            return result;
        }
        catch (OperationCanceledException)
        {
            if (store?.DidWrite == true) AgnosiaUtilities.MarkWorkProfileResetRequired();
            throw;
        }
        finally { ProvisioningLock.Release(); }
    }

    private async Task<OperationResult> StartProvisioningAsync(bool allowOffline, CancellationToken cancellationToken)
    {
        if (!await ProvisioningLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return OperationResult.Failure("Создание рабочего профиля уже выполняется.");
        try { return await StartSystemProvisioningAsync(allowOffline, cancellationToken).ConfigureAwait(false); }
        finally { ProvisioningLock.Release(); }
    }

    private async Task<OperationResult> StartSystemProvisioningAsync(bool allowOffline, CancellationToken cancellationToken)
    {
        var host = getActivityHost();
        var activity = host.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        // The system wizard must not clear an uncertain root operation or race its delayed Binder work.
        var pendingDirect = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetString(StorageKeys.DirectProfileProvisioning);
        if (pendingDirect is not null)
        {
            try
            {
                if (DirectProfileProvisioningState.Decode(pendingDirect).RequiresReboot(
                        Settings.Global.GetInt(activity.ContentResolver, Settings.Global.BootCount, -1)))
                    return OperationResult.Failure("Результат предыдущей root-команды не подтверждён. Перезагрузите устройство перед созданием профиля.");
            }
            catch (FormatException)
            {
                return OperationResult.Failure("Не удалось прочитать состояние предыдущей настройки рабочего профиля.");
            }
        }

        if (AndroidSystemApi.GetDevicePolicyManager(activity) is not { } policyManager)
            return OperationResult.Failure("На этом устройстве недоступны API политики устройства.");

        if (!AndroidProvisioningApi.CanStartManagedProfileProvisioning(policyManager))
            return CreateProvisioningBlockedResult(activity);

        if (allowOffline && !OperatingSystem.IsAndroidVersionAtLeast(33))
            return OperationResult.Failure("Создание рабочего профиля офлайн доступно на Android 13 и новее.");

        var authKey = PrepareProvisioningAuthentication();
        var intent = CreateManagedProfileProvisioningIntent(activity, host.AdminReceiverType, authKey, allowOffline);
        var result = await commandRunner.StartExternalActivityForResultAsync(
                intent,
                cancellationToken,
                AndroidActivityCommandGateway.ProvisioningActivityResultTimeout)
            .ConfigureAwait(false);
        return await CompleteProvisioningAsync(activity, result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> OpenWorkProfileSettingsAsync(CancellationToken cancellationToken = default)
    {
        var activity = GetInitializedActivity();

        var intents = new[]
        {
            new Intent(ManagedProfileSettingsAction),
            new Intent(Settings.ActionSyncSettings),
            new Intent(Settings.ActionSettings)
        };

        foreach (var intent in intents)
        {
            var result = await TryOpenSettingsIntentAsync(activity, intent, cancellationToken)
                .ConfigureAwait(false);
            if (!WasCanceledWithError(result))
                return OperationResult.Success("Проверьте удаление рабочего профиля в настройках Android.");
        }

        return OperationResult.Failure("Android не смог открыть настройки устройства.");
    }

    public void NotifyManagedProfileProvisioned(Context context, Intent? intent)
    {
        AgnosiaRuntime.Initialize(context);
        AgnosiaUtilities.MarkManagedProfileProvisioned(context, intent);
    }

    private static OperationResult CreateProvisioningBlockedResult(Activity activity)
    {
        var diagnostics = AndroidWorkProfileDiagnosticsReader.Read(activity);
        Log.Warn(LogTag, $"Managed profile provisioning blocked. {diagnostics.ToLogString()}.");

        if (diagnostics.ManagedProfileExists)
            return MarkProfileResetRequired(
                "Android не разрешает создать новый рабочий профиль, потому что в системе уже есть другой или остаточный рабочий профиль. " +
                "Если рабочий профиль виден в настройках Android, удалите его и повторите создание профиля Agnosia. " +
                "Если Android больше не показывает рабочий профиль, перезагрузите устройство и попробуйте снова.");

        return OperationResult.Failure(
            "Android сейчас не разрешает создать рабочий профиль. Проверьте ограничения устройства и повторите попытку.");
    }

    private static string PrepareProvisioningAuthentication()
    {
        ServiceRegistry.GetRequiredService<LocalStorageManager>().RemoveDurably(StorageKeys.DirectProfileProvisioning);
        var authKey = AuthenticationUtility.CreateAndStoreKey();
        AuthenticationUtility.Reset();
        AgnosiaUtilities.MarkWorkProfileSetupStarted();
        AuthenticationUtility.TryStoreProvisioningKey(authKey);
        return authKey;
    }

    private static Intent CreateManagedProfileProvisioningIntent(
        Activity activity,
        Type adminReceiverType,
        string authKey,
        bool allowOffline)
    {
        var intent = new Intent(DevicePolicyManager.ActionProvisionManagedProfile);
        AndroidProvisioningApi.ConfigureManagedProfileProvisioningIntent(
            intent,
            AgnosiaUtilities.GetAdminComponent(activity, adminReceiverType),
            authKey,
            allowOffline);
        return intent;
    }

    private async Task<OperationResult> CompleteProvisioningAsync(
        Activity activity,
        AndroidActivityResult result,
        CancellationToken cancellationToken)
    {
        if (result.ResultCode != Result.Ok)
        {
            var error = AndroidActivityResultApi.ExtractError(result);
            if (!string.IsNullOrWhiteSpace(error)) return OperationResult.Failure(error);

            if (AgnosiaUtilities.HasAssociatedProfile(activity))
                return MarkProfileResetRequired(
                    "Android создал рабочий профиль, но Agnosia не может подтвердить управление им. " +
                    "Удалите рабочий профиль в настройках Android, затем создайте его заново через Agnosia.");

            AgnosiaUtilities.ClearWorkProfileConfiguredState();
            return OperationResult.Failure("Создание рабочего профиля отменено или отклонено Android.");
        }

        if (await WaitForWorkProfileAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            AgnosiaUtilities.MarkWorkProfileReady();
            return OperationResult.Success("Рабочий профиль подключен.");
        }

        if (AgnosiaUtilities.HasAssociatedProfile(activity))
            return MarkProfileResetRequired(
                "Рабочий профиль создан, но сейчас недоступен для Agnosia. " +
                "Удалите рабочий профиль в настройках Android, затем создайте его заново через Agnosia.");

        AgnosiaUtilities.ClearWorkProfileConfiguredState();
        return OperationResult.Failure("Android не создал рабочий профиль. Запустите создание заново через Agnosia.");
    }

    private static OperationResult MarkProfileResetRequired(string message)
    {
        AgnosiaUtilities.MarkWorkProfileResetRequired();
        return OperationResult.Failure(message);
    }

    private async Task<bool> WaitForWorkProfileAvailabilityAsync(
        CancellationToken cancellationToken,
        int attempts = ProvisioningWarmupAttempts,
        int delayMilliseconds = ProvisioningWarmupDelayMilliseconds)
    {
        var activity = getActivityHost().CurrentActivity;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (AgnosiaUtilities.HasWorkProfileTarget(activity) &&
                await commandRunner.CanReachWorkProfileAsync(cancellationToken).ConfigureAwait(false)) return true;

            if (attempt < attempts - 1)
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private Activity GetInitializedActivity()
    {
        var activity = getActivityHost().CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return activity;
    }

    private static bool WasCanceledWithError(AndroidActivityResult result)
    {
        return result.ResultCode == Result.Canceled
               && !string.IsNullOrWhiteSpace(AndroidActivityResultApi.ExtractError(result));
    }

    private async Task<AndroidActivityResult> TryOpenSettingsIntentAsync(
        Activity activity,
        Intent intent,
        CancellationToken cancellationToken)
    {
        if (activity.PackageManager is not { } packageManager
            || intent.ResolveActivity(packageManager) is null)
            return AndroidActivityResultApi.CreateCanceledResult("Android не нашёл подходящий экран настроек.");

        return await commandRunner.StartExternalActivityForResultAsync(intent, cancellationToken)
            .ConfigureAwait(false);
    }
}
