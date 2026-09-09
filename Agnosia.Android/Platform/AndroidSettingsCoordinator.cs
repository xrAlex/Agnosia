using Agnosia.Android.Files;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Platform;

internal sealed class AndroidSettingsCoordinator(Func<Activity> getInitializedActivity)
{
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

    public async Task<OperationResult> OpenDocumentsUiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activity = getInitializedActivity();
        try
        {
            await AgnosiaFileShuttleClientBroker.PreconnectAsync(activity, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(exception.Message);
        }

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(null, "vnd.android.document/root");
        if (activity is not MainActivity mainActivity)
            return OperationResult.Failure("Android не смог определить активный экран Agnosia.");

        var result = await mainActivity.StartWhenResumedAsync(intent, cancellationToken)
            .ConfigureAwait(false);
        var error = AndroidActivityResultApi.ExtractError(result);
        return string.IsNullOrWhiteSpace(error)
            ? OperationResult.Success("Открываем системный файловый интерфейс.")
            : OperationResult.Failure(error);
    }
}
