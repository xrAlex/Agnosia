using System.Text.Json;
using Agnosia.Models;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

public static class AndroidProfileCommandGateway
{
    private const string LogTag = "AgnosiaProfileCommand";
    private const string ExtraPackage = "package";
    private const string ExtraIsSystem = "is_system";
    private const string ExtraPackages = "packages";
    private const string ExtraShowAll = "show_all";
    private const string ExtraName = "name";
    private const string ExtraBoolean = "boolean";
    public const string ExtraTrigger = "trigger";
    private static readonly TimeSpan ProfilePingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BooleanQueryCacheTtl = TimeSpan.FromSeconds(5);
    private static readonly Lock BooleanQueryCacheSync = new();
    private static readonly Dictionary<string, CachedBooleanQuery> BooleanQueryCache = [];

    internal static async Task<bool> CanReachWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken) =>
        (await CheckWorkProfileOwnerAsync(commandRunner, cancellationToken)).Kind
        == WorkProfileOwnerCheckKind.AppIsProfileOwner;

    internal static async Task<WorkProfileOwnerCheckResult> CheckWorkProfileOwnerAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = commandRunner.CurrentActivity;
        if (string.IsNullOrWhiteSpace(AuthenticationUtility.GetExistingKey()))
        {
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.AuthenticationKeyMissing,
                "authKey=missing");
        }

        if (!AgnosiaUtilities.HasWorkProfileTarget(activity))
        {
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.TargetUnavailable,
                "crossProfileTarget=missing");
        }

        using var pingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pingCancellation.CancelAfter(ProfilePingTimeout);
        try
        {
            var result = await commandRunner.StartActivityForResultAsync(
                new Intent(AgnosiaActions.ProfilePing),
                useWorkProfile: true,
                pingCancellation.Token);
            return InterpretProfilePingResult(result);
        }
        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn(LogTag, "Timed out waiting for work-profile ping.");
            return new WorkProfileOwnerCheckResult(
                WorkProfileOwnerCheckKind.Unreachable,
                $"profilePing=timeout:{ProfilePingTimeout.TotalMilliseconds:0}ms");
        }
    }

    internal static async Task<ProfileAppsQueryResult?> QueryAppsAsync(
        AndroidActivityCommandGateway commandRunner,
        ProfileKind profile,
        bool showAll,
        CancellationToken cancellationToken)
    {
        if (profile == ProfileKind.Personal)
        {
            return QueryLocalApps(commandRunner.CurrentActivity, showAll);
        }

        var intent = new Intent(AgnosiaActions.QueryApps);
        intent.PutExtra(ExtraShowAll, showAll);
        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            useWorkProfile: true,
            cancellationToken);
        if (result.ResultCode != Result.Ok || result.Data is null)
        {
            Log.Warn(LogTag, "Failed to query work apps through the profile activity command.");
            return null;
        }

        var apps = DeserializeResult<IReadOnlyList<AppServiceModel>>(
            result.Data.GetStringExtra(AndroidCommandContract.ResultAppsJson),
            "work apps") ?? [];
        var interactionPackages = result.Data.GetStringArrayExtra(AndroidCommandContract.ResultInteractionPackages) ?? [];
        return new ProfileAppsQueryResult(apps, interactionPackages);
    }

    internal static async Task<IReadOnlyList<AppLogEntry>> QueryWorkLogsAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.StartActivityForResultAsync(
            new Intent(AgnosiaActions.QueryLogs),
            useWorkProfile: true,
            cancellationToken);
        if (result.ResultCode != Result.Ok || result.Data is null)
        {
            Log.Warn(LogTag, "Failed to query work logs through the profile activity command.");
            return [];
        }

        return DeserializeResult<IReadOnlyList<AppLogEntry>>(
            result.Data.GetStringExtra(AndroidCommandContract.ResultLogsJson),
            "work logs") ?? [];
    }

    internal static Task<bool> QueryWorkUsageStatsAccessAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken) =>
        QueryWorkBooleanAsync(
            commandRunner,
            AgnosiaActions.QueryUsageStatsAccess,
            AndroidCommandContract.ResultUsageStatsAccess,
            cancellationToken);

    internal static Task<bool> QueryWorkPackageInstallAccessAsync(
        AndroidActivityCommandGateway commandRunner,
        CancellationToken cancellationToken) =>
        QueryWorkBooleanAsync(
            commandRunner,
            AgnosiaActions.QueryPackageInstallAccess,
            AndroidCommandContract.ResultPackageInstallAccess,
            cancellationToken);

    internal static Task<OperationResult> EnableSystemAppInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = CreateSystemPackageIntent(AgnosiaActions.InstallPackage, packageName);
        return RunWorkPackageOperationAsync(commandRunner, intent, successMessage, cancellationToken);
    }

    internal static Task<OperationResult> HideSystemAppInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = CreateSystemPackageIntent(AgnosiaActions.UninstallPackage, packageName);
        return RunWorkPackageOperationAsync(commandRunner, intent, successMessage, cancellationToken);
    }

    internal static Task<OperationResult> SetPackageHiddenInWorkProfileAsync(
        AndroidActivityCommandGateway commandRunner,
        string packageName,
        bool hidden,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = new Intent(hidden ? AgnosiaActions.FreezePackage : AgnosiaActions.UnfreezePackage);
        intent.PutExtra(ExtraPackage, packageName);
        return RunWorkPackageOperationAsync(commandRunner, intent, successMessage, cancellationToken);
    }

    internal static async Task<OperationResult> SetCrossProfileInteractionAsync(
        AndroidActivityCommandGateway commandRunner,
        string[] packages,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var intent = new Intent(AgnosiaActions.SetCrossProfileInteraction);
        intent.PutExtra(ExtraPackages, packages);
        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            useWorkProfile: true,
            cancellationToken);
        if (result.ResultCode == Result.Canceled)
        {
            return OperationResult.Failure(
                AndroidActivityResultApi.ExtractError(result)
                ?? "Android отклонил изменение межпрофильной политики.");
        }

        return result.Data?.GetBooleanExtra(AndroidCommandContract.ResultToggleSuccess, false) == true
            ? OperationResult.Success(successMessage)
            : OperationResult.Failure("Android отклонил изменение межпрофильной политики.");
    }

    public static Task<OperationResult> SynchronizeBooleanToWorkProfileAsync(
        Context context,
        string name,
        bool value,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<OperationResult>(cancellationToken);
        }

        var intent = new Intent(AgnosiaActions.SynchronizePreference);
        intent.PutExtra(ExtraName, name);
        intent.PutExtra(ExtraBoolean, value);
        return Task.FromResult(StartOtherProfileActivity(
            context,
            intent,
            "OK",
            $"Android не смог синхронизировать настройку {name} с рабочим профилем."));
    }

    public static OperationResult FreezePackageInWorkProfile(
        Context context,
        string packageName,
        string successMessage)
    {
        var intent = new Intent(AgnosiaActions.FreezePackage);
        intent.PutExtra(ExtraPackage, packageName);
        return StartOtherProfileActivity(
            context,
            intent,
            successMessage,
            $"Android не смог скрыть {packageName} в рабочем профиле.");
    }

    public static OperationResult NotifyParentWorkAppFrozen(Context context, string trigger)
    {
        var intent = new Intent(AgnosiaActions.WorkAppFrozen);
        intent.PutExtra(ExtraTrigger, trigger);
        return StartOtherProfileActivity(
            context,
            intent,
            "Основной профиль получил событие заморозки приложения.",
            "Android не смог передать событие заморозки приложения в основной профиль.");
    }

    private static async Task<bool> QueryWorkBooleanAsync(
        AndroidActivityCommandGateway commandRunner,
        string action,
        string resultExtra,
        CancellationToken cancellationToken)
    {
        if (TryGetCachedBooleanQuery(action, out var cachedValue))
        {
            return cachedValue;
        }

        var result = await commandRunner.StartActivityForResultAsync(
            new Intent(action),
            useWorkProfile: true,
            cancellationToken);
        var value = result.ResultCode == Result.Ok
            && result.Data?.GetBooleanExtra(resultExtra, false) == true;
        SetCachedBooleanQuery(action, value);
        return value;
    }

    private static async Task<OperationResult> RunWorkPackageOperationAsync(
        AndroidActivityCommandGateway commandRunner,
        Intent intent,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            useWorkProfile: true,
            cancellationToken);
        return AndroidActivityResultApi.ToPackageOperationResult(result, successMessage);
    }

    private static ProfileAppsQueryResult? QueryLocalApps(Context context, bool showAll)
    {
        if (context.PackageManager is not { } packageManager)
        {
            Log.Warn(LogTag, "PackageManager unavailable; could not query local apps.");
            return null;
        }

        var apps = AndroidAppInventoryApi.QueryInstalledApps(
            context,
            packageManager,
            AndroidSystemApi.GetDevicePolicyManager(context),
            admin: null,
            showAll);
        return new ProfileAppsQueryResult(apps, []);
    }

    private static Intent CreateSystemPackageIntent(string action, string packageName)
    {
        var intent = new Intent(action);
        intent.PutExtra(ExtraPackage, packageName);
        intent.PutExtra(ExtraIsSystem, true);
        return intent;
    }

    private static OperationResult StartOtherProfileActivity(
        Context context,
        Intent intent,
        string successMessage,
        string errorMessage)
    {
        try
        {
            AgnosiaRuntime.Initialize(context);
            intent.AddFlags(ActivityFlags.NewTask);
            AgnosiaUtilities.TransferIntentToProfile(context, intent);
            intent.AddCategory(Intent.CategoryDefault);
            Log.Debug(
                LogTag,
                $"Starting other-profile activity. action={intent.Action ?? "<none>"}, component={intent.Component?.PackageName ?? "<none>"}/{intent.Component?.ClassName ?? "<none>"}.");
            StartActivityOnMainThread(context, intent);
            return OperationResult.Success(successMessage);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"{errorMessage} Details: {exception}");
            return OperationResult.Failure(errorMessage);
        }
    }

    private static void StartActivityOnMainThread(Context context, Intent intent)
    {
        if (Looper.MainLooper?.IsCurrentThread == true)
        {
            context.StartActivity(intent);
            return;
        }

        Exception? startException = null;
        using var completed = new ManualResetEventSlim();
        var mainLooper = Looper.MainLooper
            ?? throw new InvalidOperationException("Android main looper is unavailable.");
        new Handler(mainLooper).Post(() =>
        {
            try
            {
                context.StartActivity(intent);
            }
            catch (Exception exception)
            {
                startException = exception;
            }
            finally
            {
                completed.Set();
            }
        });

        completed.Wait(TimeSpan.FromSeconds(2));
        if (startException is not null)
        {
            throw startException;
        }
    }

    private static T? DeserializeResult<T>(string? raw, string description)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to deserialize {description}: {exception.Message}");
            return default;
        }
    }

    private static bool TryGetCachedBooleanQuery(string action, out bool value)
    {
        lock (BooleanQueryCacheSync)
        {
            if (BooleanQueryCache.TryGetValue(action, out var cached)
                && DateTimeOffset.UtcNow - cached.CachedAt <= BooleanQueryCacheTtl)
            {
                value = cached.Value;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static void SetCachedBooleanQuery(string action, bool value)
    {
        lock (BooleanQueryCacheSync)
        {
            BooleanQueryCache[action] = new CachedBooleanQuery(value, DateTimeOffset.UtcNow);
        }
    }

    private sealed record CachedBooleanQuery(bool Value, DateTimeOffset CachedAt);

    private static WorkProfileOwnerCheckResult InterpretProfilePingResult(AndroidActivityResult result)
    {
        if (result.Data?.GetBooleanExtra(AndroidCommandContract.ResultProfileOwnerCheckPerformed, false) == true)
        {
            if (!AuthenticationUtility.CheckIntent(result.Data))
            {
                return new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.Unreachable,
                    "profilePing=unsignedOwnerCheck");
            }

            var isProfileOwner = result.Data.GetBooleanExtra(AndroidCommandContract.ResultIsProfileOwner, false);
            return isProfileOwner
                ? new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppIsProfileOwner,
                    "inProfileOwnerCheck=true")
                : new WorkProfileOwnerCheckResult(
                    WorkProfileOwnerCheckKind.AppInstalledButNotOwner,
                    "inProfileOwnerCheck=false");
        }

        var error = AndroidActivityResultApi.ExtractError(result);
        return new WorkProfileOwnerCheckResult(
            WorkProfileOwnerCheckKind.Unreachable,
            string.IsNullOrWhiteSpace(error)
                ? $"profilePing=result:{result.ResultCode}"
                : $"profilePing=result:{result.ResultCode}; error={error}");
    }
}

internal sealed record ProfileAppsQueryResult(
    IReadOnlyList<AppServiceModel> Apps,
    IReadOnlyList<string> InteractionPackages);

internal enum WorkProfileOwnerCheckKind
{
    AuthenticationKeyMissing,
    TargetUnavailable,
    Unreachable,
    AppInstalledButNotOwner,
    AppIsProfileOwner
}

internal sealed record WorkProfileOwnerCheckResult(
    WorkProfileOwnerCheckKind Kind,
    string DiagnosticReason);
