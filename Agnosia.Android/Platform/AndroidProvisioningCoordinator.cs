using Agnosia.Models;
using Android.App.Admin;
using Android.Content;
using Android.OS;
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

    public async Task<OperationResult> StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        var host = getActivityHost();
        var activity = host.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (AndroidSystemApi.GetDevicePolicyManager(activity) is not { } policyManager)
            return OperationResult.Failure("На этом устройстве недоступны API политики устройства.");

        if (!AndroidProvisioningApi.CanStartManagedProfileProvisioning(policyManager))
            return CreateProvisioningBlockedResult(activity);

        var authKey = PrepareProvisioningAuthentication();
        var intent = CreateManagedProfileProvisioningIntent(activity, host.AdminReceiverType, authKey);
        var result = await commandRunner.StartExternalActivityForResultAsync(intent, cancellationToken);
        return await CompleteProvisioningAsync(activity, result, cancellationToken);
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
            var result = await TryOpenSettingsIntentAsync(activity, intent, cancellationToken);
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
        var authKey = AuthenticationUtility.CreateAndStoreKey();
        AuthenticationUtility.Reset();
        AgnosiaUtilities.MarkWorkProfileSetupStarted();
        AuthenticationUtility.TryStoreProvisioningKey(authKey);
        return authKey;
    }

    private static Intent CreateManagedProfileProvisioningIntent(
        Activity activity,
        Type adminReceiverType,
        string authKey)
    {
        var intent = new Intent(DevicePolicyManager.ActionProvisionManagedProfile);
        AndroidProvisioningApi.ConfigureManagedProfileProvisioningIntent(
            intent,
            AgnosiaUtilities.GetAdminComponent(activity, adminReceiverType),
            authKey);
        return intent;
    }

    private async Task<OperationResult> CompleteProvisioningAsync(
        Activity activity,
        AndroidActivityResult result,
        CancellationToken cancellationToken)
    {
        if (result.ResultCode != Result.Ok)
        {
            if (AgnosiaUtilities.HasAssociatedProfile(activity))
                return MarkProfileResetRequired(
                    "Android создал рабочий профиль, но Agnosia не может подтвердить управление им. " +
                    "Удалите рабочий профиль в настройках Android, затем создайте его заново через Agnosia.");

            AgnosiaUtilities.ClearWorkProfileConfiguredState();
            return OperationResult.Failure("Создание рабочего профиля отменено или отклонено Android.");
        }

        if (await WaitForWorkProfileAvailabilityAsync(cancellationToken))
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
                await commandRunner.CanReachWorkProfileAsync(cancellationToken)) return true;

            if (attempt < attempts - 1) await Task.Delay(delayMilliseconds, cancellationToken);
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

        return await commandRunner.StartExternalActivityForResultAsync(intent, cancellationToken);
    }
}
