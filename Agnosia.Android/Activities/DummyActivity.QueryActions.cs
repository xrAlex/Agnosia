using System.Text.Json;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Packages;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Android.App;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
using OperationCanceledException = System.OperationCanceledException;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
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
            var showAll = intent.GetBooleanExtra("show_all", false);
            var policyManager = _policyManager;
            var admin = _isProfileOwner && policyManager is not null
                ? AgnosiaUtilities.GetAdminComponent(this, AdminReceiverType)
                : null;
            var models = await Task.Run(() => AndroidAppInventoryApi.QueryInstalledApps(
                this,
                packageManager,
                policyManager,
                admin,
                showAll,
                cancellationToken), cancellationToken);

            var result = new Intent();
            result.PutExtra(AndroidCommandContract.ResultAppsJson, JsonSerializer.Serialize(models));

            if (admin is not null && policyManager is not null)
                result.PutExtra(AndroidCommandContract.ResultInteractionPackages,
                    AndroidPolicyApi.GetCrossProfilePackages(policyManager, admin));

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

    private async Task ActionQueryAppIconAsync(CancellationToken cancellationToken)
    {
        var intent = Intent;
        var packageManager = PackageManager;
        var packageName = intent?.GetStringExtra("package");
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
                packageName), cancellationToken);
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
        var packageNames = Intent?.GetStringArrayExtra("packages") ?? [];
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
            }, cancellationToken);

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
            JsonSerializer.Serialize(AndroidAppLogArchive.Load(this)));
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
}
