using Agnosia.Models;
using Avalonia.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel
{
    private void StartInventoryLoad(
        DashboardSnapshot profileSnapshot,
        bool preserveCurrentWorkAppsOnEmpty = false,
        bool showProgress = true)
    {
        var generation = BeginInventoryLoad(out var inventoryCancellation);
        if (showProgress)
        {
            HasLoadedInventory = false;
            IsInventoryLoading = true;
        }

        StatusIsError = false;
        _ = LoadInventoryForGenerationAsync(
            profileSnapshot,
            generation,
            inventoryCancellation,
            preserveCurrentWorkAppsOnEmpty);
    }

    private void StartInventoryLoadIfNeeded()
    {
        if (!IsDashboardVisible
            || HasLoadedInventory
            || _inventoryLoadInProgress
            || _lastProfileSnapshot is null)
            return;

        StartInventoryLoad(
            _lastProfileSnapshot,
            showProgress: SelectedSection == DashboardSection.Apps);
    }

    private int BeginInventoryLoad(out CancellationTokenSource inventoryCancellation)
    {
        CancelInventoryLoad(false);
        inventoryCancellation = new CancellationTokenSource();
        _inventoryLoadCancellation = inventoryCancellation;
        _inventoryLoadInProgress = true;
        return ++_inventoryLoadGeneration;
    }

    private void CancelInventoryLoad(bool updateProgressState)
    {
        if (_inventoryLoadCancellation is not null)
        {
            _inventoryLoadCancellation.Cancel();
            _inventoryLoadCancellation = null;
        }

        ++_inventoryLoadGeneration;
        _inventoryLoadInProgress = false;
        if (updateProgressState) IsInventoryLoading = false;
    }

    private async Task LoadInventoryForGenerationAsync(
        DashboardSnapshot profileSnapshot,
        int generation,
        CancellationTokenSource inventoryCancellation,
        bool preserveCurrentWorkAppsOnEmpty)
    {
        try
        {
            var loadStartedAt = Stopwatch.GetTimestamp();
            var inventory = await _dashboardService
                .LoadAppInventoryAsync(profileSnapshot, inventoryCancellation.Token)
                .ConfigureAwait(false);
            TracePerf(
                "LoadInventory",
                loadStartedAt,
                $"personal={inventory.PersonalApps.Count}; work={inventory.WorkApps.Count}");

            await InvokeOnUiThreadActionAsync(() =>
            {
                if (!IsCurrentInventoryLoad(generation, inventoryCancellation)) return;

                ApplyInventorySnapshot(preserveCurrentWorkAppsOnEmpty
                    ? PreserveCurrentWorkAppsOnEmpty(profileSnapshot, inventory)
                    : inventory);
                HasLoadedInventory = true;
                IsInventoryLoading = false;
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (inventoryCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await InvokeOnUiThreadActionAsync(() =>
            {
                if (!IsCurrentInventoryLoad(generation, inventoryCancellation)) return;

                HasLoadedInventory = true;
                IsInventoryLoading = false;
                StatusIsError = true;
                StatusMessage = ResolveExceptionMessage(ex, "LoadAppsFailed");
            }, DispatcherPriority.Background);
        }
        finally
        {
            await InvokeOnUiThreadActionAsync(() =>
            {
                if (ReferenceEquals(_inventoryLoadCancellation, inventoryCancellation))
                {
                    _inventoryLoadCancellation = null;
                    _inventoryLoadInProgress = false;
                    IsInventoryLoading = false;
                }
            }, DispatcherPriority.Background);
            inventoryCancellation.Dispose();
        }
    }

    private bool IsCurrentInventoryLoad(int generation, CancellationTokenSource inventoryCancellation)
    {
        return generation == _inventoryLoadGeneration
               && ReferenceEquals(_inventoryLoadCancellation, inventoryCancellation)
               && !inventoryCancellation.IsCancellationRequested;
    }
}
