using Agnosia.Models;
using Avalonia.Threading;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel
{
    private void StartOnboardingMonitorIfNeeded()
    {
        if (OnboardingCompleted
            || OnboardingStep == OnboardingStep.Welcome
            || OnboardingStep == OnboardingStep.Permissions
            || OnboardingStep == OnboardingStep.Final
            || _isOperationInProgress
            || _onboardingMonitorCancellation is not null)
            return;

        if (OnboardingStep == OnboardingStep.WorkProfile && !IsSettingUp && !WorkProfileAvailable) return;

        _onboardingMonitorCancellation = new CancellationTokenSource();
        _ = MonitorOnboardingAsync(_onboardingMonitorCancellation.Token);
    }

    private void StopOnboardingMonitor()
    {
        _onboardingMonitorCancellation?.Cancel();
    }

    private async Task MonitorOnboardingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !OnboardingCompleted)
            {
                if (_isOperationInProgress)
                {
                    await _delayAsync(TimeSpan.FromMilliseconds(OnboardingMonitorDelayMs), cancellationToken);
                    continue;
                }

                await InvokeOnUiThreadAsync(() => AdvanceOnboardingAsync(cancellationToken));

                if (OnboardingCompleted
                    || OnboardingStep == OnboardingStep.Welcome
                    || OnboardingStep == OnboardingStep.Permissions
                    || OnboardingStep == OnboardingStep.Final)
                    return;

                await _delayAsync(TimeSpan.FromMilliseconds(OnboardingMonitorDelayMs), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await ReportErrorOnUiThreadAsync(ex, "UpdateState");
        }
        finally
        {
            if (_onboardingMonitorCancellation?.Token == cancellationToken)
            {
                _onboardingMonitorCancellation.Dispose();
                _onboardingMonitorCancellation = null;
            }
        }
    }

    private static async Task InvokeOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action();
            return;
        }

        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action();
                completionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        }, DispatcherPriority.Background);

        await completionSource.Task;
    }

    private async Task AdvanceOnboardingAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _isOperationInProgress) return;

        if (OnboardingStep == OnboardingStep.WorkProfile)
        {
            SetPreparingOnboardingPermissions(true);
            try
            {
                if (!WorkProfileAvailable)
                {
                    await RefreshAsync();
                    if (!WorkProfileAvailable)
                    {
                        SetPreparingOnboardingPermissions(false);
                        return;
                    }
                }

                await EnsurePermissionsLoadedAsync();
                OnboardingStep = OnboardingStep.Permissions;
                await CompleteOnboardingIfReadyAsync();
                return;
            }
            finally
            {
                SetPreparingOnboardingPermissions(false);
            }
        }

        if (OnboardingStep == OnboardingStep.Permissions)
        {
            await ReloadPermissionsAsync();
            await CompleteOnboardingIfReadyAsync();
        }
    }

    private async Task CompleteOnboardingIfReadyAsync()
    {
        if (OnboardingStep == OnboardingStep.Permissions && AreOnboardingPermissionsGranted)
            OnboardingStep = OnboardingStep.Final;
    }

    private void SetPreparingOnboardingPermissions(bool value)
    {
        if (_isPreparingOnboardingPermissions == value) return;

        _isPreparingOnboardingPermissions = value;
        OnPropertyChanged(nameof(IsOnboardingWorkProfileStep));
        OnPropertyChanged(nameof(IsOnboardingPermissionsStep));
        OnPropertyChanged(nameof(OnboardingStepLabel));
    }
}
