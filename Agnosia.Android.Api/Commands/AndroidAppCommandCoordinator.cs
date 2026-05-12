using Agnosia.Models;
using Android.Content;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

internal sealed class AndroidAppCommandCoordinator(
    AndroidActivityCommandGateway commandRunner,
    AndroidPermissionCoordinator permissionCoordinator,
    Func<CancellationToken, Task<DashboardSnapshot>> loadDashboardAsync)
{
    private const string LogTag = "AgnosiaTransientVpn";
    private const string ActivityResultLogTag = "AgnosiaActivityResult";

    public async Task<OperationResult> CloneAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        OperationResult result;
        if (app.IsSystem && app.Profile == ProfileKind.Personal)
        {
            result = await AndroidProfileCommandGateway.EnableSystemAppInWorkProfileAsync(
                commandRunner,
                app.PackageName,
                "Приложение скопировано в рабочий профиль.",
                cancellationToken);
        }
        else
        {
            var intent = new Intent(AgnosiaActions.InstallPackage);
            intent.PutExtra("package", app.PackageName);
            intent.PutExtra("is_system", app.IsSystem);

            if (!app.IsSystem)
            {
                intent.PutExtra("apk", app.SourceDirectory);
                intent.PutExtra("split_apks", app.SplitApks.ToArray());
            }

            result = await (app.Profile == ProfileKind.Personal
                ? commandRunner.RunPackageOperationAsync(intent, useWorkProfile: true, cancellationToken, "Приложение скопировано в рабочий профиль.")
                : commandRunner.RunPackageOperationAsync(intent, useWorkProfile: false, cancellationToken, "Приложение скопировано в личный профиль."));
        }

        if (!result.Succeeded || app.Profile != ProfileKind.Personal)
        {
            return result;
        }

        var freezeResult = await HideClonedWorkAppAsync(
            app with
            {
                Profile = ProfileKind.Work,
                IsHidden = false,
                CanLaunch = true,
                IsInstalled = true
            },
            cancellationToken);

        return !freezeResult.Succeeded
            ? OperationResult.Failure($"Приложение скопировано, но скрыть его не удалось: {freezeResult.Message}")
            : OperationResult.Success("Приложение скопировано в рабочий профиль.");
    }

    public Task<OperationResult> UninstallAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        if (app.IsSystem && app.Profile == ProfileKind.Work)
        {
            return AndroidProfileCommandGateway.HideSystemAppInWorkProfileAsync(
                commandRunner,
                app.PackageName,
                "Приложение удалено из рабочего профиля.",
                cancellationToken);
        }

        var intent = new Intent(AgnosiaActions.UninstallPackage);
        intent.PutExtra("package", app.PackageName);
        intent.PutExtra("is_system", app.IsSystem);

        return app.Profile == ProfileKind.Work
            ? commandRunner.RunPackageOperationAsync(intent, useWorkProfile: true, cancellationToken, "Приложение удалено из рабочего профиля.")
            : commandRunner.RunPackageOperationAsync(intent, useWorkProfile: false, cancellationToken, "Приложение удалено из личного профиля.");
    }

    public async Task<OperationResult> SetFrozenAsync(
        AppSnapshot app,
        bool hidden,
        CancellationToken cancellationToken)
    {
        if (hidden)
        {
            await permissionCoordinator.EnsureUsageStatsAccessRequestedAsync(cancellationToken);
            var shortcutResult = await PreparePinnedShortcutInParentAsync(app.PackageName, cancellationToken);
            if (!shortcutResult.Succeeded || !shortcutResult.HideImmediately)
            {
                return new OperationResult(shortcutResult.Succeeded, shortcutResult.Message);
            }
        }

        return await AndroidProfileCommandGateway.SetPackageHiddenInWorkProfileAsync(
            commandRunner,
            app.PackageName,
            hidden,
            hidden ? "Приложение скрыто." : "Приложение восстановлено.",
            cancellationToken);
    }

    private async Task<OperationResult> HideClonedWorkAppAsync(
        AppSnapshot app,
        CancellationToken cancellationToken)
    {
        await permissionCoordinator.EnsureUsageStatsAccessRequestedAsync(cancellationToken);

        var shortcutResult = await PreparePinnedShortcutInParentAsync(app.PackageName, cancellationToken);
        if (!shortcutResult.Succeeded)
        {
            return new OperationResult(false, shortcutResult.Message);
        }

        return await AndroidProfileCommandGateway.SetPackageHiddenInWorkProfileAsync(
            commandRunner,
            app.PackageName,
            hidden: true,
            "Приложение скрыто.",
            cancellationToken);
    }

    public Task<OperationResult> ForceFreezeAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        if (app.Profile != ProfileKind.Work)
        {
            return Task.FromResult(OperationResult.Failure("Политика устройства может скрывать только приложения рабочего профиля."));
        }

        return AndroidProfileCommandGateway.SetPackageHiddenInWorkProfileAsync(
            commandRunner,
            app.PackageName,
            hidden: true,
            "Приложение скрыто.",
            cancellationToken);
    }

    public async Task<OperationResult> CreateShortcutAsync(
        AppSnapshot app,
        CancellationToken cancellationToken)
    {
        if (app.Profile != ProfileKind.Work)
        {
            return OperationResult.Failure("Ярлыки доступны только для приложений рабочего профиля.");
        }

        var shortcutResult = await PreparePinnedShortcutInParentAsync(app.PackageName, cancellationToken);
        return shortcutResult.Succeeded
            ? OperationResult.Success(shortcutResult.Message)
            : OperationResult.Failure(shortcutResult.Message);
    }

    public async Task<OperationResult> LaunchAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (app.Profile == ProfileKind.Personal)
        {
            var launchIntent = activity.PackageManager?.GetLaunchIntentForPackage(app.PackageName);
            if (launchIntent is null)
            {
                return OperationResult.Failure("Android не смог определить, как открыть это приложение.");
            }

            if (AndroidIntentApi.TryStartActivity(
                activity,
                launchIntent,
                ActivityResultLogTag,
                "Android не смог открыть это приложение.",
                out var error))
            {
                return OperationResult.Success("Открываем приложение.");
            }

            return OperationResult.Failure(error ?? "Android не смог открыть это приложение.");
        }

        var vpnPreparationResult = await EnsurePersonalVpnDisabledBeforeWorkLaunchAsync(cancellationToken);
        if (!vpnPreparationResult.Succeeded)
        {
            return vpnPreparationResult;
        }

        var intent = new Intent(AgnosiaActions.UnfreezeAndLaunch);
        intent.PutExtra("packageName", app.PackageName);
        intent.PutExtra(
            AndroidCommandContract.ExtraParentFrozenCallback,
            commandRunner.CreateWorkAppFrozenCallbackPendingIntent(app.PackageName));
        return await commandRunner.RunVoidOperationAsync(intent, useWorkProfile: true, cancellationToken, "Открываем приложение.");
    }

    public async Task<OperationResult> SetInteractionAccessAsync(
        AppSnapshot app,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!enabled && AndroidPackageAccessPolicy.RequiresCrossProfileInteraction(app.PackageName))
        {
            return OperationResult.Success("Для этого приложения контроль доступа всегда отключен политикой Agnosia.");
        }

        var snapshot = await loadDashboardAsync(cancellationToken);
        var packages = snapshot.WorkApps
            .Where(item => item.InteractionAllowed)
            .Select(item => item.PackageName)
            .ToHashSet(StringComparer.Ordinal);

        if (enabled)
        {
            packages.Add(app.PackageName);
        }
        else
        {
            packages.Remove(app.PackageName);
        }

        return await AndroidProfileCommandGateway.SetCrossProfileInteractionAsync(
            commandRunner,
            packages.ToArray(),
            enabled ? "Межпрофильный обмен включен." : "Межпрофильный обмен отключен.",
            cancellationToken);
    }

    private async Task<OperationResult> EnsurePersonalVpnDisabledBeforeWorkLaunchAsync(CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        var storage = LocalStorageManager.Instance;
        if (!storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch))
        {
            storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            Log.Info(LogTag, "Disable-VPN-before-launch is disabled in settings.");
            return new OperationResult(true, string.Empty);
        }

        if (!AndroidVpnApi.IsVpnActive(activity))
        {
            storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            Log.Info(LogTag, "No active VPN detected, continuing without the transient VPN service.");
            return new OperationResult(true, string.Empty);
        }

        storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
        Log.Info(LogTag, "Active VPN detected, starting transient VpnService.");
        var disconnectResult = await TransientVpnDisconnectService.DisconnectActiveVpnAsync(
            commandRunner,
            cancellationToken);
        if (!disconnectResult.Succeeded)
        {
            Log.Warn(LogTag, "Transient VpnService failed to disconnect the active VPN.");
            return disconnectResult;
        }

        activity = commandRunner.CurrentActivity;
        if (AndroidVpnApi.IsVpnActive(activity))
        {
            return OperationResult.Failure("VPN все еще активен в личном профиле. Сторонний клиент мог сразу подключиться снова.");
        }

        storage.SetBoolean(StorageKeys.HaveActiveVpnSession, true);
        return OperationResult.Success("VPN отключен.");
    }

    private async Task<ShortcutPreparationResult> PreparePinnedShortcutInParentAsync(
        string packageName,
        CancellationToken cancellationToken)
    {
        var prepareIntent = new Intent(AgnosiaActions.PrepareHiddenShortcut);
        prepareIntent.PutExtra("package", packageName);

        var prepareResult = await commandRunner.StartActivityForResultAsync(prepareIntent, useWorkProfile: true, cancellationToken);
        if (prepareResult.ResultCode != Result.Ok || prepareResult.Data is null)
        {
            var error = prepareResult.Data?.GetStringExtra(AndroidCommandContract.ResultError);
            return ShortcutPreparationResult.Failure(string.IsNullOrWhiteSpace(error)
                ? "Android не смог подготовить данные ярлыка для скрытого приложения."
                : error);
        }

        var createIntent = new Intent(AgnosiaActions.CreateHiddenShortcut);
        CopyExtraIfPresent(prepareResult.Data, createIntent, "packageName");
        CopyExtraIfPresent(prepareResult.Data, createIntent, "targetActivity");
        CopyExtraIfPresent(prepareResult.Data, createIntent, "label");
        CopyExtraIfPresent(prepareResult.Data, createIntent, "iconBase64");
        CopyExtraIfPresent(prepareResult.Data, createIntent, "shortcutToken");

        var createResult = await commandRunner.StartActivityForResultAsync(createIntent, useWorkProfile: false, cancellationToken);
        if (createResult.ResultCode != Result.Ok)
        {
            var error = createResult.Data?.GetStringExtra(AndroidCommandContract.ResultError);
            return ShortcutPreparationResult.Failure(string.IsNullOrWhiteSpace(error)
                ? "Лаунчер отклонил создание ярлыка скрытого приложения."
                : error);
        }

        return new ShortcutPreparationResult(
            Succeeded: true,
            HideImmediately: createResult.Data?.GetBooleanExtra(AndroidCommandContract.ResultHideImmediately, false) == true,
            Message: createResult.Data?.GetStringExtra(AndroidCommandContract.ResultMessage)
                ?? "Подготовка ярлыка завершена.");
    }

    private static void CopyExtraIfPresent(Intent source, Intent target, string extraName)
    {
        var value = source.GetStringExtra(extraName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.PutExtra(extraName, value);
        }
    }

    private sealed record ShortcutPreparationResult(
        bool Succeeded,
        bool HideImmediately,
        string Message)
    {
        public static ShortcutPreparationResult Failure(string message) => new(false, false, message);
    }
}
