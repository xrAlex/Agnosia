using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

public static class AndroidVpnAutomationApi
{
    private const string LogTag = "AgnosiaVpnAutomation";
    private const string CallerHint = "Agnosia";

    private static readonly VpnClientDefinition[] VpnClients =
    [
        new(
            VpnAutomationClientKind.ClashMeta,
            "Clash Meta for Android",
            "com.github.metacubex.clash.meta",
            "com.github.metacubex.clash.meta.action.START_CLASH",
            ActivityClassName: "com.github.kr328.clash.ExternalControlActivity",
            RequireExplicitActivity: true),
        new(
            VpnAutomationClientKind.Happ,
            "Happ",
            "com.happproxy",
            string.Empty,
            ToggleAction: "com.happproxy.action.widget.click",
            ReceiverClassName: "com.happproxy.receiver.WidgetProvider"),
        new(
            VpnAutomationClientKind.Tunguska,
            "Tunguska",
            "io.acionyx.tunguska",
            "io.acionyx.tunguska.action.AUTOMATION_START",
            ActivityClassName: "io.acionyx.tunguska.app.AutomationRelayActivity",
            RequireExplicitActivity: true),
        new(
            VpnAutomationClientKind.FlClash,
            "FlClash",
            "com.follow.clash",
            "com.follow.clash.action.START",
            ActivityClassName: "com.follow.clash.TempActivity",
            RequireExplicitActivity: true,
            IsolateActivityTask: true),
        new(
            VpnAutomationClientKind.Incy,
            "INCY",
            "llc.itdev.incy",
            "llc.itdev.incy.CONNECT",
            "llc.itdev.incy.DISCONNECT",
            ReceiverClassName: "llc.itdev.incy.receiver.VpnIntentReceiver"),
        new(
            VpnAutomationClientKind.Exclave,
            "Exclave",
            "com.github.dyhkwong.sagernet",
            string.Empty,
            ActivityClassName: "io.nekohasekai.sagernet.QuickToggleShortcut",
            RequireExplicitActivity: true),
        new(
            VpnAutomationClientKind.Husi,
            "husi",
            "fr.husi",
            string.Empty,
            ActivityClassName: "fr.husi.QuickToggleShortcut",
            RequireExplicitActivity: true),
        new(
            VpnAutomationClientKind.NekoBoxPlus,
            "NekoBox",
            ["com.nb4a.plus", "moe.nb4a"],
            string.Empty,
            activityClassName: "io.nekohasekai.sagernet.QuickToggleShortcut",
            requireExplicitActivity: true)
    ];

    public static Task<OperationResult> EnableConfiguredVpnAfterWorkFreezeAsync(Context context, string trigger)
    {
        AgnosiaRuntime.Initialize(context);
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        if (!storage.GetBoolean(StorageKeys.EnableVpnAfterWorkFreeze))
        {
            Log.Debug(LogTag, $"Enable-after-freeze is disabled. trigger={trigger}.");
            return Task.FromResult(OperationResult.Success(string.Empty));
        }

        var hadActiveVpnSession = storage.GetBoolean(StorageKeys.HaveActiveVpnSession);
        if (!hadActiveVpnSession)
        {
            Log.Debug(LogTag,
                $"Enable-after-freeze is disabled, VPN was not enabled before it was disabled. trigger={trigger}, hadActiveVpnSession={hadActiveVpnSession}.");
            return Task.FromResult(OperationResult.Success(string.Empty));
        }

        if (AndroidVpnApi.IsVpnActive(context))
        {
            storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            Log.Debug(LogTag, $"VPN is already active; skipping enable-after-freeze command. trigger={trigger}.");
            return Task.FromResult(OperationResult.Success(string.Empty));
        }

        var definition = ResolveInstalledPackage(context, ResolveClient(AndroidSettingsStore.LoadVpnAfterWorkFreezeClient(storage)));
        Log.Debug(LogTag,
            $"Starting VPN after work freeze. client={definition.DisplayName}, trigger={trigger}, hadActiveVpnSession={hadActiveVpnSession}.");

        try
        {
            var result = StartClient(context, definition, storage);
            if (result.Succeeded) storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);

            return Task.FromResult(result);
        }
        catch (ActivityNotFoundException)
        {
            var message = $"VPN-клиент {definition.DisplayName} не найден или не принимает команду запуска.";
            Log.Warn(LogTag, $"{message} package={definition.PackageName}.");
            return Task.FromResult(OperationResult.Failure(message));
        }
        catch (Exception exception)
        {
            var message = $"Не удалось отправить команду запуска VPN для {definition.DisplayName}.";
            Log.Warn(LogTag, $"{message} error={exception.Message}");
            return Task.FromResult(OperationResult.Failure(message));
        }
    }

    private static OperationResult StartClient(
        Context context,
        VpnClientDefinition definition,
        LocalStorageManager storage)
    {
        switch (definition.Kind)
        {
            case VpnAutomationClientKind.Happ:
                SendToggleBroadcast(context, definition);
                Log.Warn(
                    LogTag,
                    "Happ command is toggle-only; it can disable VPN if the client is already connected.");
                return CreateStartCommandSuccess(definition);
            case VpnAutomationClientKind.Tunguska:
                return StartTunguska(context, definition, storage);
            case VpnAutomationClientKind.Incy:
                SendStartBroadcast(context, definition);
                return CreateStartCommandSuccess(definition);
            default:
                return StartActivityClient(context, definition);
        }
    }

    private static OperationResult StartActivityClient(Context context, VpnClientDefinition definition)
    {
        var intent = CreateStartActivityIntent(definition, definition.RequireExplicitActivity);
        if (definition.IsolateActivityTask) AddIsolatedActivityTaskFlags(intent);

        if (AndroidIntentApi.TryStartActivity(
                context,
                intent,
                LogTag,
                $"VPN-клиент {definition.DisplayName} не найден или не принимает команду запуска.",
                out var error))
        {
            Log.Info(LogTag,
                $"StartActivity command sent. client={definition.DisplayName}, action={definition.StartAction}.");
            return CreateStartCommandSuccess(definition);
        }

        if (definition.RequireExplicitActivity || string.IsNullOrWhiteSpace(definition.ActivityClassName))
            return OperationResult.Failure(error ??
                                           $"VPN-клиент {definition.DisplayName} не найден или не принимает команду запуска.");

        var fallbackIntent = CreateStartActivityIntent(definition, true);
        if (definition.IsolateActivityTask) AddIsolatedActivityTaskFlags(fallbackIntent);

        if (!AndroidIntentApi.TryStartActivity(
                context,
                fallbackIntent,
                LogTag,
                $"VPN-клиент {definition.DisplayName} не найден или не принимает резервную команду запуска.",
                out error))
            return OperationResult.Failure(error ?? $"VPN-клиент {definition.DisplayName} не найден или не принимает команду запуска.");
        
        Log.Info(LogTag, $"StartActivity fallback command sent. client={definition.DisplayName}, component={definition.ActivityClassName}.");
        
        return CreateStartCommandSuccess(definition);

    }

    private static Intent CreateStartActivityIntent(VpnClientDefinition definition, bool useComponent)
    {
        var intent = string.IsNullOrWhiteSpace(definition.StartAction)
            ? new Intent()
            : new Intent(definition.StartAction);
        if (!string.IsNullOrWhiteSpace(definition.StartAction)) intent.AddCategory(Intent.CategoryDefault);

        intent.SetPackage(definition.PackageName);
        intent.AddFlags(ActivityFlags.NewTask);
        if (useComponent && !string.IsNullOrWhiteSpace(definition.ActivityClassName))
            intent.SetComponent(new ComponentName(definition.PackageName, definition.ActivityClassName));

        return intent;
    }

    private static void AddIsolatedActivityTaskFlags(Intent intent)
    {
        intent.AddFlags(
            ActivityFlags.MultipleTask
            | ActivityFlags.NoHistory
            | ActivityFlags.ExcludeFromRecents
            | ActivityFlags.NoAnimation);
    }

    private static void SendToggleBroadcast(Context context, VpnClientDefinition definition)
    {
        var toggleAction = definition.ToggleAction
                           ?? throw new InvalidOperationException(
                               "VPN client definition does not provide a toggle action.");
        var receiverClassName = definition.ReceiverClassName
                                ?? throw new InvalidOperationException(
                                    "VPN client definition does not provide a receiver class.");
        var intent = new Intent(toggleAction);
        intent.SetPackage(definition.PackageName);
        intent.SetComponent(new ComponentName(definition.PackageName, receiverClassName));
        context.SendBroadcast(intent);
        Log.Info(LogTag, $"Toggle broadcast sent. client={definition.DisplayName}, action={toggleAction}.");
    }

    private static void SendStartBroadcast(Context context, VpnClientDefinition definition)
    {
        var receiverClassName = definition.ReceiverClassName
                                ?? throw new InvalidOperationException(
                                    "VPN client definition does not provide a receiver class.");
        var intent = new Intent(definition.StartAction);
        intent.SetPackage(definition.PackageName);
        intent.SetComponent(new ComponentName(definition.PackageName, receiverClassName));
        context.SendBroadcast(intent);
        Log.Info(LogTag, $"Start broadcast sent. client={definition.DisplayName}, action={definition.StartAction}.");
    }

    private static OperationResult StartTunguska(Context context, VpnClientDefinition definition,
        LocalStorageManager storage)
    {
        var token = storage.GetString(StorageKeys.TunguskaAutomationToken)?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return OperationResult.Failure($"Не удалось отправить команду запуска VPN для {definition.DisplayName}.");

        var intent = CreateStartActivityIntent(definition, true);
        intent.PutExtra("automation_token", token);
        intent.PutExtra("caller_hint", CallerHint);
        if (!AndroidIntentApi.TryStartActivity(
                context,
                intent,
                LogTag,
                $"VPN-клиент {definition.DisplayName} не найден или не принимает команду запуска.",
                out var error))
            return OperationResult.Failure(error ??
                                           $"Не удалось отправить команду запуска VPN для {definition.DisplayName}.");

        Log.Info(LogTag, "Tunguska automation start command sent.");
        return CreateStartCommandSuccess(definition);
    }

    private static OperationResult CreateStartCommandSuccess(VpnClientDefinition definition)
    {
        return OperationResult.Success($"Команда запуска VPN отправлена в {definition.DisplayName}.");
    }

    private static VpnClientDefinition ResolveClient(VpnAutomationClientKind kind)
    {
        foreach (var definition in VpnClients)
            if (definition.Kind == kind)
                return definition;

        return VpnClients[^1];
    }

    private static VpnClientDefinition ResolveInstalledPackage(Context context, VpnClientDefinition definition)
    {
        if (definition.PackageNames.Length <= 1) return definition;

        foreach (var packageName in definition.PackageNames)
            if (IsPackageInstalled(context.PackageManager, packageName))
                return definition with { PackageName = packageName };

        return definition;
    }

    private static bool IsPackageInstalled(PackageManager? packageManager, string packageName)
    {
        if (packageManager is null || string.IsNullOrWhiteSpace(packageName)) return false;

        try
        {
            packageManager.GetPackageInfo(packageName, 0);
            return true;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    private sealed record VpnClientDefinition(
        VpnAutomationClientKind Kind,
        string DisplayName,
        string PackageName,
        string StartAction,
        string? StopAction = null,
        string? ToggleAction = null,
        string? ActivityClassName = null,
        string? ReceiverClassName = null,
        bool RequireExplicitActivity = false,
        bool IsolateActivityTask = false)
    {
        public string[] PackageNames { get; init; } = [PackageName];

        public VpnClientDefinition(
            VpnAutomationClientKind kind,
            string displayName,
            string[] packageNames,
            string startAction,
            string? stopAction = null,
            string? toggleAction = null,
            string? activityClassName = null,
            string? receiverClassName = null,
            bool requireExplicitActivity = false,
            bool isolateActivityTask = false)
            : this(
                kind,
                displayName,
                packageNames.Length > 0 ? packageNames[0] : string.Empty,
                startAction,
                stopAction,
                toggleAction,
                activityClassName,
                receiverClassName,
                requireExplicitActivity,
                isolateActivityTask)
        {
            PackageNames = packageNames;
        }
    }
}
