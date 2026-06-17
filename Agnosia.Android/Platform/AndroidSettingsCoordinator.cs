using Agnosia.Android.Files;
using Agnosia.Models;
using Android.App;
using Android.Content;

namespace Agnosia.Android.Platform;

internal sealed class AndroidSettingsCoordinator(Func<Activity> getInitializedActivity)
{
    private const string LogTag = "AgnosiaPlatformBridge";

    public Task<bool> LoadOnboardingCompletedAsync(CancellationToken cancellationToken = default)
    {
        _ = getInitializedActivity();
        return Task.FromResult(ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.OnboardingCompleted));
    }

    public Task<OperationResult> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        _ = getInitializedActivity();
        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.OnboardingCompleted, true);
        return Task.FromResult(OperationResult.Success("Первичная настройка завершена."));
    }

    public Task<OperationResult> SaveSettingsAsync(
        AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        var activity = getInitializedActivity();
        return AndroidSettingsStore.SaveAsync(activity, settings, cancellationToken);
    }

    public Task<OperationResult> OpenDocumentsUiAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<OperationResult>(cancellationToken);

        var activity = getInitializedActivity();
        try
        {
            AgnosiaFileShuttleClientBroker.Preconnect(activity);
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult.Failure(exception.Message));
        }

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(null, "vnd.android.document/root");

        return Task.FromResult(AndroidIntentApi.TryStartActivity(
            activity,
            intent,
            LogTag,
            "Android не смог открыть системный файловый интерфейс.",
            out var error)
            ? OperationResult.Success("Открываем системный файловый интерфейс.")
            : OperationResult.Failure(error ?? "Android не смог открыть системный файловый интерфейс."));
    }
}
