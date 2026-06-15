using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Serialization;
using Agnosia.Android.Api.Storage;
using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using OperationCanceledException = System.OperationCanceledException;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private const int DefaultQueryAppsPageLimit = 100;
    private const int DefaultQueryAppsMaxJsonBytes = 512 * 1024;
    private static readonly TimeSpan QueryAppsCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly Lock QueryAppsCacheSync = new();
    private static readonly Dictionary<string, CachedAppInventoryQuery> QueryAppsCache = [];

    private async Task ActionQueryAppsAsync(CancellationToken cancellationToken)
    {
        var intent = Intent;
        var packageManager = PackageManager;
        if (intent is null || packageManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        try
        {
            var showAll = intent.GetBooleanExtra(AndroidCommandContract.ExtraShowAll, false);
            var isRiskEngineEnabled = LocalStorageManager.Instance.GetBoolean(StorageKeys.RiskEngineEnabled, true);
            var policyManager = _policyManager;
            var admin = _isProfileOwner && policyManager is not null
                ? AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType)
                : null;
            var inventory = await GetOrCreateCachedAppInventoryQueryAsync(
                    showAll,
                    isRiskEngineEnabled,
                    intent.GetStringExtra(AndroidCommandContract.ExtraQueryPageToken),
                    packageManager,
                    policyManager,
                    admin,
                    cancellationToken)
                .ConfigureAwait(false);

            var result = CreateQueryAppsResult(intent, inventory);
            FinishWithResult(Result.Ok, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query apps: {exception}");
            FinishWithResult(Result.Canceled);
        }
    }

    private async Task<CachedAppInventoryQuery> GetOrCreateCachedAppInventoryQueryAsync(
        bool showAll,
        bool isRiskEngineEnabled,
        string? pageToken,
        PackageManager packageManager,
        DevicePolicyManager? policyManager,
        ComponentName? admin,
        CancellationToken cancellationToken)
    {
        if (TryGetCachedAppInventoryQuery(pageToken, showAll, isRiskEngineEnabled, out var cachedQuery))
            return cachedQuery;

        var models = await Task.Run(() => AndroidAppInventoryApi.QueryInstalledApps(
            this,
            packageManager,
            policyManager,
            admin,
            showAll,
            cancellationToken,
            AppInventoryQueryOptions.WorkList), cancellationToken).ConfigureAwait(false);

        var interactionPackages = admin is not null && policyManager is not null
            ? AndroidPolicyApi.GetCrossProfilePackages(policyManager, admin)
            : [];
        var query = new CachedAppInventoryQuery(
            showAll,
            isRiskEngineEnabled,
            DateTimeOffset.UtcNow,
            models,
            interactionPackages);
        CacheAppInventoryQuery(pageToken, query);
        return query;
    }

    private static Intent CreateQueryAppsResult(
        Intent request,
        CachedAppInventoryQuery inventory)
    {
        var result = new Intent();
        if (!IsPagedQuery(request))
        {
            result.PutExtra(
                AndroidCommandContract.ResultAppsJson,
                JsonSerializer.Serialize(inventory.Apps.ToList(), AndroidApiJsonContext.Default.ListAppServiceModel));
            result.PutExtra(AndroidCommandContract.ResultInteractionPackages, inventory.InteractionPackages);
            return result;
        }

        var page = AppInventoryPayloadPager.CreatePage(
            inventory.Apps,
            request.GetIntExtra(AndroidCommandContract.ExtraQueryOffset, 0),
            request.GetIntExtra(AndroidCommandContract.ExtraQueryLimit, DefaultQueryAppsPageLimit),
            request.GetIntExtra(AndroidCommandContract.ExtraQueryMaxJsonBytes, DefaultQueryAppsMaxJsonBytes));

        result.PutExtra(AndroidCommandContract.ResultAppsJson, page.Json);
        result.PutExtra(AndroidCommandContract.ResultNextQueryOffset, page.NextOffset);
        result.PutExtra(AndroidCommandContract.ResultQueryHasMore, page.HasMore);
        result.PutExtra(AndroidCommandContract.ResultQueryTotalCount, page.TotalCount);

        if (page.Offset == 0)
            result.PutExtra(AndroidCommandContract.ResultInteractionPackages, inventory.InteractionPackages);

        Log.Debug(
            LogTag,
            $"Query apps page prepared. offset={page.Offset}, nextOffset={page.NextOffset}, hasMore={page.HasMore}, pageCount={page.Apps.Count}, totalCount={page.TotalCount}, jsonBytes={page.JsonUtf8Bytes}.");
        return result;
    }

    private static bool IsPagedQuery(Intent request)
    {
        return request.HasExtra(AndroidCommandContract.ExtraQueryPageToken)
               || request.HasExtra(AndroidCommandContract.ExtraQueryOffset)
               || request.HasExtra(AndroidCommandContract.ExtraQueryLimit)
               || request.HasExtra(AndroidCommandContract.ExtraQueryMaxJsonBytes);
    }

    private static bool TryGetCachedAppInventoryQuery(
        string? pageToken,
        bool showAll,
        bool isRiskEngineEnabled,
        out CachedAppInventoryQuery query)
    {
        query = null!;
        if (string.IsNullOrWhiteSpace(pageToken)) return false;

        var now = DateTimeOffset.UtcNow;
        lock (QueryAppsCacheSync)
        {
            PruneExpiredAppInventoryQueries(now);
            if (!QueryAppsCache.TryGetValue(pageToken, out var cached)
                || cached.ShowAll != showAll
                || cached.RiskEngineEnabled != isRiskEngineEnabled)
            {
                QueryAppsCache.Remove(pageToken);
                return false;
            }

            query = cached;
            return true;
        }
    }

    private static void CacheAppInventoryQuery(
        string? pageToken,
        CachedAppInventoryQuery query)
    {
        if (string.IsNullOrWhiteSpace(pageToken)) return;

        lock (QueryAppsCacheSync)
        {
            PruneExpiredAppInventoryQueries(DateTimeOffset.UtcNow);
            QueryAppsCache[pageToken] = query;
        }
    }

    private static void PruneExpiredAppInventoryQueries(DateTimeOffset now)
    {
        foreach (var (key, query) in QueryAppsCache.ToArray())
            if (now - query.CachedAt > QueryAppsCacheTtl)
                QueryAppsCache.Remove(key);
    }

    private static void ClearAppInventoryQueryCache()
    {
        lock (QueryAppsCacheSync)
        {
            QueryAppsCache.Clear();
        }
    }

    private async Task ActionQueryAppIconAsync(CancellationToken cancellationToken)
    {
        var intent = Intent;
        var packageManager = PackageManager;
        var packageName = intent?.GetStringExtra(AndroidCommandContract.ExtraPackage);
        if (string.IsNullOrWhiteSpace(packageName) || packageManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        try
        {
            var iconPng = await Task.Run(() => AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(
                this,
                packageManager,
                packageName), cancellationToken).ConfigureAwait(false);
            var result = new Intent();
            if (iconPng is { Length: > 0 }) result.PutExtra(AndroidCommandContract.ResultIconPng, iconPng);

            FinishWithResult(Result.Ok, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query app icon for {packageName}: {exception}");
            FinishWithResult(Result.Canceled);
        }
    }

    private async Task ActionQueryAppIconsAsync(CancellationToken cancellationToken)
    {
        var packageManager = PackageManager;
        var packageNames = Intent?.GetStringArrayExtra(AndroidCommandContract.ExtraPackages) ?? [];
        if (packageNames.Length == 0 || packageManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        try
        {
            var icons = await Task.Run(() =>
            {
                var loadedIcons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
                foreach (var packageName in packageNames.Distinct(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    loadedIcons[packageName] = AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(
                        this,
                        packageManager,
                        packageName);
                }

                return loadedIcons;
            }, cancellationToken).ConfigureAwait(false);

            var result = new Intent();
            var bundle = new Bundle();
            foreach (var (packageName, iconPng) in icons)
                if (iconPng is { Length: > 0 })
                    bundle.PutByteArray(packageName, iconPng);

            result.PutExtra(AndroidCommandContract.ResultIconsBundle, bundle);
            FinishWithResult(Result.Ok, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to query app icons: {exception}");
            FinishWithResult(Result.Canceled);
        }
    }

    private void ActionQueryLogs()
    {
        var result = new Intent();
        result.PutExtra(
            AndroidCommandContract.ResultLogsJson,
            JsonSerializer.Serialize(
                AndroidAppLogArchive.Load(this).ToList(),
                AndroidApiJsonContext.Default.ListAppLogEntry));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionQueryCrossProfilePackages()
    {
        if (!_isProfileOwner || _policyManager is null)
        {
            FinishWithResult(Result.Canceled);
            return;
        }

        var result = new Intent();
        result.PutExtra(
            AndroidCommandContract.ResultInteractionPackages,
            AndroidPolicyApi.GetCrossProfilePackages(
                _policyManager,
                AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType)));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionQueryPermissions()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultUsageStatsAccess,
            AndroidUsageStatsAccessApi.HasAccess(this, LogTag));
        result.PutExtra(AndroidCommandContract.ResultPackageInstallAccess,
            AndroidPackageApi.CanRequestInstalls(this, LogTag));
        result.PutExtra(AndroidCommandContract.ResultAllFilesAccess,
            AndroidPermissionApi.HasAllFilesAccess(this));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionQueryUsageStatsAccess()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultUsageStatsAccess,
            AndroidUsageStatsAccessApi.HasAccess(this, LogTag));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionRequestUsageStatsAccess()
    {
        if (AndroidUsageStatsAccessApi.HasAccess(this, LogTag))
        {
            FinishWithSuccessMessage("Доступ к истории использования уже включен.");
            return;
        }

        if (AndroidUsageStatsAccessApi.TryOpenSettings(this, LogTag, FinishWithError))
            FinishWithSuccessMessage(
                "Откройте Agnosia в настройках Android и включите доступ к истории использования.");
    }

    private void ActionQueryPackageInstallAccess()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultPackageInstallAccess,
            AndroidPackageApi.CanRequestInstalls(this, LogTag));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionRequestPackageInstallAccess()
    {
        if (AndroidPackageApi.CanRequestInstalls(this, LogTag))
        {
            FinishWithSuccessMessage("Доступ к установке APK уже включен.");
            return;
        }

        if (AndroidPackageApi.TryOpenUnknownSourcesSettings(this, LogTag, FinishWithError))
            FinishWithSuccessMessage("Включите установку APK из Agnosia в рабочем профиле.");
    }

    private void ActionQueryAllFilesAccess()
    {
        var result = new Intent();
        result.PutExtra(AndroidCommandContract.ResultAllFilesAccess,
            AndroidPermissionApi.HasAllFilesAccess(this));
        FinishWithResult(Result.Ok, result);
    }

    private void ActionRequestAllFilesAccess()
    {
        if (AndroidPermissionApi.HasAllFilesAccess(this))
        {
            FinishWithSuccessMessage("Доступ ко всем файлам уже включен.");
            return;
        }

        var result = AndroidPermissionApi.OpenAllFilesAccessSettings(this);
        if (result.Succeeded)
            FinishWithSuccessMessage(result.Message);
        else
            FinishWithError(result.Message);
    }

    private sealed record CachedAppInventoryQuery(
        bool ShowAll,
        bool RiskEngineEnabled,
        DateTimeOffset CachedAt,
        IReadOnlyList<AppServiceModel> Apps,
        string[] InteractionPackages);
}
