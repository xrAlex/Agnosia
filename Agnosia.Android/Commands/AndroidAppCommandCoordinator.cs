using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Commands;

internal sealed class AndroidAppCommandCoordinator(
    AndroidActivityCommandGateway commandRunner,
    AndroidPermissionCoordinator permissionCoordinator)
{
    private const string LogTag = "AgnosiaTransientVpn";
    private const string ActivityResultLogTag = "AgnosiaActivityResult";
    private static readonly TimeSpan ClonedPackageSettleDelay = TimeSpan.FromSeconds(2);
    private static readonly string[] HiddenShortcutPayloadExtras =
    [
        AndroidCommandContract.ExtraLaunchPackageName,
        AndroidCommandContract.ExtraShortcutTargetActivity,
        AndroidCommandContract.ExtraShortcutLabel,
        AndroidCommandContract.ExtraShortcutIconBase64,
        AndroidCommandContract.ExtraShortcutToken
    ];

    public async Task<OperationResult> CloneAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        OperationResult result;
        if (app.IsSystem && app.Profile == ProfileKind.Personal)
        {
            result = await AndroidProfileCommandGateway.EnableSystemAppInWorkProfileAsync(
                commandRunner,
                app.PackageName,
                "Приложение скопировано в рабочий профиль.",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var intent = CreatePackageIntent(AgnosiaActions.InstallPackage, app);

            if (!app.IsSystem)
            {
                var sourceDirectory = app.SourceDirectory;
                var splitApks = app.SplitApks.ToArray();
                if (app.Profile == ProfileKind.Personal)
                {
                    var sourceResolution = await ResolveInstalledPackageSourceAsync(
                            commandRunner.CurrentActivity.PackageManager,
                            app.PackageName,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (sourceResolution.Succeeded)
                    {
                        sourceDirectory = sourceResolution.SourceDirectory;
                        splitApks = sourceResolution.SplitApks;
                    }
                    else
                    {
                        sourceDirectory = null;
                        splitApks = [];
                    }
                }

                intent.PutExtra(AndroidCommandContract.ExtraApk, sourceDirectory);
                intent.PutExtra(AndroidCommandContract.ExtraSplitApks, splitApks);
            }

            result = await (app.Profile == ProfileKind.Personal
                ? commandRunner.RunPackageOperationAsync(intent, true, cancellationToken,
                    "Приложение скопировано в рабочий профиль.")
                : commandRunner.RunPackageOperationAsync(intent, false, cancellationToken,
                    "Приложение скопировано в личный профиль."))
                .ConfigureAwait(false);
        }

        if (!result.Succeeded || app.Profile != ProfileKind.Personal) return result;

        var freezeResult = await HideClonedWorkAppAsync(
            app with
            {
                Profile = ProfileKind.Work,
                IsHidden = false,
                CanLaunch = true,
                IsInstalled = true
            },
            cancellationToken).ConfigureAwait(false);

        return !freezeResult.Succeeded
            ? OperationResult.Failure($"Приложение скопировано, но скрыть его не удалось: {freezeResult.Message}")
            : OperationResult.Success("Приложение скопировано в рабочий профиль.");
    }

    public async Task<OperationResult> UninstallAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        OperationResult result;
        if (app.IsSystem && app.Profile == ProfileKind.Work)
        {
            result = await AndroidProfileCommandGateway.HideSystemAppInWorkProfileAsync(
                commandRunner,
                app.PackageName,
                "Приложение удалено из рабочего профиля.",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var intent = CreatePackageIntent(AgnosiaActions.UninstallPackage, app);

            result = await (app.Profile == ProfileKind.Work
                ? commandRunner.RunPackageOperationAsync(intent, true, cancellationToken,
                    "Приложение удалено из рабочего профиля.")
                : commandRunner.RunPackageOperationAsync(intent, false, cancellationToken,
                    "Приложение удалено из личного профиля."))
                .ConfigureAwait(false);
        }

        if (!result.Succeeded || app.Profile != ProfileKind.Work) return result;

        var shortcutResult = AndroidHiddenShortcutApi.InvalidatePinnedShortcut(
            commandRunner.CurrentActivity,
            app.PackageName);

        return OperationResult.Success($"{result.Message} {shortcutResult.Message}");
    }

    public async Task<OperationResult> SetFrozenAsync(
        AppSnapshot app,
        bool hidden,
        CancellationToken cancellationToken)
    {
        if (hidden && app is { Profile: ProfileKind.Work, IsSystem: true })
            return OperationResult.Success("Системные приложения рабочего профиля не замораживаются Agnosia.");

        ShortcutPreparationResult? shortcutResult = null;
        if (hidden)
        {
            await permissionCoordinator.EnsureUsageStatsAccessRequestedAsync(cancellationToken).ConfigureAwait(false);
            shortcutResult = await PreparePinnedShortcutInParentAsync(app.PackageName, app.IsSystem, cancellationToken)
                .ConfigureAwait(false);
            if (!shortcutResult.Succeeded || !shortcutResult.HideImmediately)
                return new OperationResult(shortcutResult.Succeeded, shortcutResult.Message);
        }

        var hideResult = await AndroidProfileCommandGateway.SetPackageHiddenInWorkProfileAsync(
            commandRunner,
            app.PackageName,
            hidden,
            hidden ? "Приложение скрыто." : "Приложение восстановлено.",
            cancellationToken).ConfigureAwait(false);
        return hideResult.Succeeded || shortcutResult is null
            ? hideResult
            : OperationResult.Failure(FormatShortcutCreatedButHideFailed(shortcutResult, hideResult.Message));
    }

    private async Task<OperationResult> HideClonedWorkAppAsync(
        AppSnapshot app,
        CancellationToken cancellationToken)
    {
        if (app.IsSystem)
            return OperationResult.Success("Системное приложение включено в рабочем профиле без заморозки.");

        await permissionCoordinator.EnsureUsageStatsAccessRequestedAsync(cancellationToken).ConfigureAwait(false);
        Log.Info(
            ActivityResultLogTag,
            $"Waiting for cloned work package to settle before hidden-shortcut preparation. package={app.PackageName}, delayMs={ClonedPackageSettleDelay.TotalMilliseconds:0}.");
        await Task.Delay(ClonedPackageSettleDelay, cancellationToken).ConfigureAwait(false);

        var shortcutResult = await PreparePinnedShortcutInParentAsync(app.PackageName, app.IsSystem, cancellationToken)
            .ConfigureAwait(false);
        if (!shortcutResult.Succeeded) return new OperationResult(false, shortcutResult.Message);

        var hideResult = await AndroidProfileCommandGateway.SetPackageHiddenInWorkProfileAsync(
            commandRunner,
            app.PackageName,
            true,
            "Приложение скрыто.",
            cancellationToken).ConfigureAwait(false);
        return hideResult.Succeeded
            ? hideResult
            : OperationResult.Failure(FormatShortcutCreatedButHideFailed(shortcutResult, hideResult.Message));
    }

    public Task<OperationResult> ForceFreezeAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        if (app.Profile != ProfileKind.Work)
            return Task.FromResult(
                OperationResult.Failure("Политика устройства может скрывать только приложения рабочего профиля."));

        if (app.IsSystem)
            return Task.FromResult(
                OperationResult.Success("Системные приложения рабочего профиля не замораживаются Agnosia."));

        return AndroidProfileCommandGateway.SetPackageHiddenInWorkProfileAsync(
            commandRunner,
            app.PackageName,
            true,
            "Приложение скрыто.",
            cancellationToken);
    }

    public async Task<OperationResult> CreateShortcutAsync(
        AppSnapshot app,
        CancellationToken cancellationToken)
    {
        if (app.Profile != ProfileKind.Work)
            return OperationResult.Failure("Ярлыки доступны только для приложений рабочего профиля.");

        var shortcutResult = await PreparePinnedShortcutInParentAsync(app.PackageName, app.IsSystem, cancellationToken)
            .ConfigureAwait(false);
        if (!shortcutResult.Succeeded) return OperationResult.Failure(shortcutResult.Message);

        return string.IsNullOrWhiteSpace(shortcutResult.PreHideError)
            ? OperationResult.Success(shortcutResult.Message)
            : OperationResult.Failure(FormatShortcutCreatedButHideFailed(
                shortcutResult,
                shortcutResult.PreHideError));
    }

    public async Task<OperationResult> LaunchAsync(AppSnapshot app, CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (app.Profile == ProfileKind.Personal)
        {
            var launchIntent = activity.PackageManager?.GetLaunchIntentForPackage(app.PackageName);
            if (launchIntent is null)
                return OperationResult.Failure("Android не смог определить, как открыть это приложение.");

            return AndroidIntentApi.TryStartActivity(
                activity,
                launchIntent,
                ActivityResultLogTag,
                "Android не смог открыть это приложение.",
                out var error) 
                ? OperationResult.Success("Открываем приложение.") 
                : OperationResult.Failure(error ?? "Android не смог открыть это приложение.");
        }

        if (!app.IsSystem)
        {
            var vpnPreparationResult = await EnsurePersonalVpnDisabledBeforeWorkLaunchAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!vpnPreparationResult.Succeeded) return vpnPreparationResult;
        }

        var intent = new Intent(AgnosiaActions.UnfreezeAndLaunch);
        intent.PutExtra(AndroidCommandContract.ExtraLaunchPackageName, app.PackageName);
        intent.PutExtra(AndroidCommandContract.ExtraLaunchDisplayName, app.Label);
        intent.PutExtra(AndroidCommandContract.ExtraIsSystem, app.IsSystem);
        if (!app.IsSystem)
            intent.PutExtra(
                AndroidCommandContract.ExtraParentFrozenCallback,
                commandRunner.CreateWorkAppFrozenCallbackPendingIntent(app.PackageName));
        return await commandRunner.RunVoidOperationAsync(intent, true, cancellationToken, "Открываем приложение.")
            .ConfigureAwait(false);
    }

    public async Task<OperationResult> SetInteractionAccessAsync(
        AppSnapshot app,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!enabled && AndroidPackageAccessPolicy.RequiresCrossProfileInteraction(app.PackageName))
            return OperationResult.Success("Для этого приложения контроль доступа всегда отключен политикой Agnosia.");

        var packages = (await AndroidProfileCommandGateway
                .QueryWorkCrossProfilePackagesAsync(commandRunner, cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        if (enabled)
            packages.Add(app.PackageName);
        else
            packages.Remove(app.PackageName);

        return await AndroidProfileCommandGateway.SetCrossProfileInteractionAsync(
            commandRunner,
            packages.ToArray(),
            enabled ? "Межпрофильный обмен включен." : "Межпрофильный обмен отключен.",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult> SetLockdownInternetAccessAsync(
        AppSnapshot app,
        bool blocked,
        CancellationToken cancellationToken)
    {
        if (app.Profile != ProfileKind.Work)
            return Task.FromResult(OperationResult.Failure("Lockdown доступен только для приложений рабочего профиля."));

        if (app.IsSystem)
            return Task.FromResult(OperationResult.Failure("Lockdown не применяется к системным приложениям рабочего профиля."));

        return AndroidProfileCommandGateway.SetLockdownInternetAccessAsync(
            commandRunner,
            app.PackageName,
            blocked,
            cancellationToken);
    }

    private async Task<OperationResult> EnsurePersonalVpnDisabledBeforeWorkLaunchAsync(
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        if (!storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch))
        {
            storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            Log.Info(LogTag, "Disable-VPN-before-launch is disabled in settings.");
            return new OperationResult(true, string.Empty);
        }

        Log.Info(LogTag, "VPN Guard is enabled for parent launch.");
        if (!await IsVpnActiveAsync(activity, cancellationToken).ConfigureAwait(false))
        {
            storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            Log.Info(LogTag, "No active VPN detected, continuing without the transient VPN service.");
            return new OperationResult(true, string.Empty);
        }

        Intent? prepareIntent;
        try
        {
            prepareIntent = VpnService.Prepare(activity);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to prepare VPN permission request before parent launch: {exception}");
            return OperationResult.Failure("Android не смог открыть запрос доступа к VPN.");
        }

        if (prepareIntent is not null)
        {
            var prepareResult = await commandRunner.StartExternalActivityForResultAsync(prepareIntent, cancellationToken)
                .ConfigureAwait(false);

            if (prepareResult.ResultCode != Result.Ok)
                return OperationResult.Failure("Android не выдал Agnosia временное управление VPN.");
        }

        activity = commandRunner.CurrentActivity;
        storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
        if (!await IsVpnActiveAsync(activity, cancellationToken).ConfigureAwait(false))
        {
            storage.SetBoolean(StorageKeys.HaveActiveVpnSession, true);
            commandRunner.ShowVpnGuardOverlay();
            Log.Debug(LogTag, "Active VPN was cleared while preparing VPN control.");
            return OperationResult.Success("VPN отключен.");
        }

        var vpnBaseline = AndroidVpnApi.GetVisibleVpnNetworkHandles(activity);
        Log.Debug(LogTag, "Active VPN detected, starting transient VpnService.");
        var disconnectResult = await TransientVpnDisconnectCoordinator.DisconnectActiveVpnAsync(
            commandRunner,
            cancellationToken).ConfigureAwait(false);
        if (!disconnectResult.Succeeded)
        {
            Log.Warn(LogTag, "Transient VpnService failed to disconnect the active VPN.");
            return disconnectResult;
        }

        activity = commandRunner.CurrentActivity;
        if (await IsVpnActiveAsync(activity, vpnBaseline, cancellationToken).ConfigureAwait(false))
            return OperationResult.Failure(
                "VPN все еще активен в личном профиле. Сторонний клиент мог сразу подключиться снова.");

        storage.SetBoolean(StorageKeys.HaveActiveVpnSession, true);
        commandRunner.ShowVpnGuardOverlay();
        return OperationResult.Success("VPN отключен.");
    }

    private async Task<ShortcutPreparationResult> PreparePinnedShortcutInParentAsync(
        string packageName,
        bool isSystem,
        CancellationToken cancellationToken)
    {
        var prepareIntent = new Intent(AgnosiaActions.PrepareHiddenShortcut);
        prepareIntent.PutExtra(AndroidCommandContract.ExtraPackage, packageName);
        prepareIntent.PutExtra(AndroidCommandContract.ExtraIsSystem, isSystem);

        var prepareResult = await commandRunner.StartActivityForResultAsync(prepareIntent, true, cancellationToken)
            .ConfigureAwait(false);
        if (prepareResult.ResultCode != Result.Ok || prepareResult.Data is null)
        {
            var error = prepareResult.Data?.GetStringExtra(AndroidCommandContract.ResultError);
            return ShortcutPreparationResult.Failure(string.IsNullOrWhiteSpace(error)
                ? "Android не смог подготовить данные ярлыка для скрытого приложения."
                : error);
        }

        var preHideSucceeded = prepareResult.Data.GetBooleanExtra(
            AndroidCommandContract.ResultPreHideSucceeded,
            true);
        var preHideError = prepareResult.Data.GetStringExtra(AndroidCommandContract.ResultError);

        var createIntent = new Intent(AgnosiaActions.CreateHiddenShortcut);
        foreach (var extraName in HiddenShortcutPayloadExtras)
            CopyExtraIfPresent(prepareResult.Data, createIntent, extraName);
        createIntent.PutExtra(
            AndroidCommandContract.ExtraIsSystem,
            prepareResult.Data.GetBooleanExtra(AndroidCommandContract.ExtraIsSystem, false));

        var createResult = await commandRunner.StartActivityForResultAsync(createIntent, false, cancellationToken)
            .ConfigureAwait(false);
        if (createResult.ResultCode != Result.Ok)
        {
            var error = createResult.Data?.GetStringExtra(AndroidCommandContract.ResultError);
            return ShortcutPreparationResult.Failure(string.IsNullOrWhiteSpace(error)
                ? "Лаунчер отклонил создание ярлыка скрытого приложения."
                : error);
        }

        return new ShortcutPreparationResult(
            true,
            createResult.Data?.GetBooleanExtra(AndroidCommandContract.ResultHideImmediately, false) == true,
            createResult.Data?.GetStringExtra(AndroidCommandContract.ResultMessage)
            ?? "Подготовка ярлыка завершена.",
            preHideSucceeded ? null : preHideError);
    }

    private static Intent CreatePackageIntent(string action, AppSnapshot app)
    {
        var intent = new Intent(action);
        intent.PutExtra(AndroidCommandContract.ExtraPackage, app.PackageName);
        intent.PutExtra(AndroidCommandContract.ExtraIsSystem, app.IsSystem);
        return intent;
    }

    private static Task<PackageSourceResolution> ResolveInstalledPackageSourceAsync(
        PackageManager? packageManager,
        string packageName,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AndroidPackageApi.TryResolveInstalledPackageSource(
                packageManager,
                packageName,
                out var sourceDirectory,
                out var splitApks)
                ? new PackageSourceResolution(true, sourceDirectory, splitApks)
                : new PackageSourceResolution(false, null, []);
        }, cancellationToken);
    }

    private static Task<bool> IsVpnActiveAsync(Context context, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AndroidVpnApi.IsVpnActive(context);
        }, cancellationToken);
    }

    private static Task<bool> IsVpnActiveAsync(
        Context context,
        IReadOnlySet<long> ignoredVpnNetworkHandles,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AndroidVpnApi.IsVpnActive(context, ignoredVpnNetworkHandles);
        }, cancellationToken);
    }

    private static void CopyExtraIfPresent(Intent source, Intent target, string extraName)
    {
        var value = source.GetStringExtra(extraName);
        if (!string.IsNullOrWhiteSpace(value)) target.PutExtra(extraName, value);
    }

    private static string FormatShortcutCreatedButHideFailed(
        ShortcutPreparationResult shortcutResult,
        string hideError)
    {
        return $"{shortcutResult.Message} Но Android не смог скрыть приложение: {hideError}";
    }

    private sealed record ShortcutPreparationResult(
        bool Succeeded,
        bool HideImmediately,
        string Message,
        string? PreHideError = null)
    {
        public static ShortcutPreparationResult Failure(string message)
        {
            return new ShortcutPreparationResult(false, false, message);
        }
    }

    private sealed record PackageSourceResolution(
        bool Succeeded,
        string? SourceDirectory,
        string[] SplitApks);
}
