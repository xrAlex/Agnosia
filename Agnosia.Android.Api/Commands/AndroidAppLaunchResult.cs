using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Models;
using Android.Content;
using ActivityNotFoundException = Android.Content.ActivityNotFoundException;
using Exception = System.Exception;

namespace Agnosia.Android.Api;

public sealed record AndroidAppLaunchResult(
    string PackageName,
    string DisplayName,
    AndroidAppLaunchStage Stage,
    bool Succeeded,
    AndroidAppLaunchIssueKind Issue,
    string Message,
    AndroidAppLaunchEvent[] Events)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public static AndroidAppLaunchResult CommandReceived(string? packageName, string? displayName)
    {
        var normalizedPackage = NormalizePackageName(packageName);
        var normalizedDisplay = NormalizeDisplayName(displayName, normalizedPackage);
        return new AndroidAppLaunchResult(
                normalizedPackage,
                normalizedDisplay,
                AndroidAppLaunchStage.CommandReceived,
                Succeeded: false,
                AndroidAppLaunchIssueKind.None,
                string.Empty,
                [])
            .WithStage(AndroidAppLaunchStage.CommandReceived, "command_received");
    }

    public AndroidAppLaunchResult WithDisplayName(string? displayName) =>
        this with
        {
            DisplayName = NormalizeDisplayName(displayName, PackageName)
        };

    public AndroidAppLaunchResult WithStage(AndroidAppLaunchStage stage, string? detail = null)
    {
        var message = BuildStageMessage(stage, DisplayName);
        return this with
        {
            Stage = stage,
            Succeeded = Succeeded || (Issue == AndroidAppLaunchIssueKind.None && IsSuccessfulStage(stage)),
            Message = message,
            Events = AppendEvent(stage, Issue, message, detail)
        };
    }

    public AndroidAppLaunchResult WithIssue(
        AndroidAppLaunchIssueKind issue,
        string? detail = null,
        bool fatal = false,
        string? message = null)
    {
        var resolvedMessage = string.IsNullOrWhiteSpace(message)
            ? BuildIssueMessage(issue, DisplayName)
            : message;
        return this with
        {
            Succeeded = fatal ? false : Succeeded,
            Issue = issue,
            Message = resolvedMessage,
            Events = AppendEvent(Stage, issue, resolvedMessage, detail)
        };
    }

    public AndroidAppLaunchResult Fail(
        AndroidAppLaunchStage stage,
        AndroidAppLaunchIssueKind issue,
        string? detail = null,
        string? message = null)
    {
        var resolvedMessage = string.IsNullOrWhiteSpace(message)
            ? BuildIssueMessage(issue, DisplayName)
            : message;
        return this with
        {
            Stage = stage,
            Succeeded = false,
            Issue = issue,
            Message = resolvedMessage,
            Events = AppendEvent(stage, issue, resolvedMessage, detail)
        };
    }

    public OperationResult ToOperationResult() =>
        Succeeded
            ? OperationResult.Success(Message)
            : OperationResult.Failure(Message);

    public Intent ToIntent()
    {
        var intent = new Intent();
        WriteToIntent(intent);
        return intent;
    }

    public AndroidActivityResult ToActivityResult() =>
        new(Succeeded ? Result.Ok : Result.Canceled, ToIntent());

    public void WriteToIntent(Intent intent)
    {
        intent.PutExtra(AndroidCommandContract.ResultLaunchJson, ToJson());
        if (Succeeded)
        {
            intent.PutExtra(AndroidCommandContract.ResultMessage, Message);
            return;
        }

        intent.PutExtra(AndroidCommandContract.ResultError, Message);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public void Log(string tag) =>
        AgnosiaLog.Info(
            tag,
            $"Launch result. package={PackageName}, displayName={DisplayName}, stage={Stage}, succeeded={Succeeded}, issue={Issue}, message={Message}");

    public static bool TryRead(Intent? intent, out AndroidAppLaunchResult result)
    {
        var raw = intent?.GetStringExtra(AndroidCommandContract.ResultLaunchJson);
        if (string.IsNullOrWhiteSpace(raw))
        {
            result = CommandReceived(null, null);
            return false;
        }

        try
        {
            result = JsonSerializer.Deserialize<AndroidAppLaunchResult>(raw, JsonOptions)
                     ?? CommandReceived(null, null);
            return true;
        }
        catch (JsonException)
        {
            result = CommandReceived(null, null);
            return false;
        }
    }

    public static AndroidAppLaunchIssueKind ClassifyStartActivityException(Exception exception)
    {
        if (exception is ActivityNotFoundException)
        {
            return AndroidAppLaunchIssueKind.MissingLauncherActivity;
        }

        return HasBackgroundActivityLaunchSignal(exception)
            ? AndroidAppLaunchIssueKind.BackgroundActivityLaunchBlocked
            : AndroidAppLaunchIssueKind.StartActivityException;
    }

    private AndroidAppLaunchEvent[] AppendEvent(
        AndroidAppLaunchStage stage,
        AndroidAppLaunchIssueKind issue,
        string message,
        string? detail) =>
        [
            ..Events,
            new AndroidAppLaunchEvent(
                stage,
                issue,
                message,
                detail,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        ];

    private static bool IsSuccessfulStage(AndroidAppLaunchStage stage) =>
        stage is AndroidAppLaunchStage.StartActivityAttempted
            or AndroidAppLaunchStage.TargetBecameForeground
            or AndroidAppLaunchStage.PackageRehidden;

    private static string BuildStageMessage(AndroidAppLaunchStage stage, string displayName) =>
        stage switch
        {
            AndroidAppLaunchStage.CommandReceived =>
                $"Команда запуска получена для {displayName}.",
            AndroidAppLaunchStage.PackageUnhidden =>
                $"{displayName} восстановлено в рабочем профиле.",
            AndroidAppLaunchStage.LaunchIntentResolved =>
                $"Экран запуска {displayName} найден.",
            AndroidAppLaunchStage.StartActivityAttempted =>
                $"Попытка запуска {displayName} выполнена.",
            AndroidAppLaunchStage.StartActivityFailedWithException =>
                $"Android не смог открыть {displayName}.",
            AndroidAppLaunchStage.TargetBecameForeground =>
                $"{displayName} вышло на передний план.",
            AndroidAppLaunchStage.PackageRehidden =>
                $"{displayName} снова скрыто в рабочем профиле.",
            _ => $"Состояние запуска {displayName}: {stage}."
        };

    private static string BuildIssueMessage(AndroidAppLaunchIssueKind issue, string displayName) =>
        issue switch
        {
            AndroidAppLaunchIssueKind.QuietMode =>
                $"Рабочий профиль на паузе. Включите его в Android и повторите запуск {displayName}.",
            AndroidAppLaunchIssueKind.MissingLauncherActivity =>
                $"Не найдена launcher-активность для {displayName}.",
            AndroidAppLaunchIssueKind.BackgroundActivityLaunchBlocked =>
                $"Android заблокировал запуск {displayName} из фона. Откройте Agnosia на экране и повторите запуск.",
            AndroidAppLaunchIssueKind.HiddenOrSuspendedPackageState =>
                $"Android оставил {displayName} скрытым или приостановленным; запуск невозможен.",
            AndroidAppLaunchIssueKind.UsageAccessDenied =>
                $"Попытка запуска {displayName} выполнена, но Agnosia не может подтвердить передний план: включите доступ к истории использования в рабочем профиле.",
            AndroidAppLaunchIssueKind.WorkProfileUnavailable =>
                $"Рабочий профиль не ответил на команду запуска {displayName}.",
            AndroidAppLaunchIssueKind.DevicePolicyManagerUnavailable =>
                "Android не предоставил сервис политики устройства.",
            AndroidAppLaunchIssueKind.PackageManagerUnavailable =>
                "Android не предоставил сервис пакетов.",
            AndroidAppLaunchIssueKind.InvalidRequest =>
                "Команда запуска не содержит корректный пакет приложения.",
            AndroidAppLaunchIssueKind.StartActivityException =>
                $"Android не смог открыть {displayName}.",
            _ => $"Android не смог открыть {displayName}."
        };

    private static bool HasBackgroundActivityLaunchSignal(Exception exception)
    {
        var message = exception.ToString();
        return message.Contains("background activity", StringComparison.OrdinalIgnoreCase)
            || message.Contains("background start", StringComparison.OrdinalIgnoreCase)
            || message.Contains("BAL", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not allowed to start", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePackageName(string? packageName) =>
        string.IsNullOrWhiteSpace(packageName) ? "<unknown>" : packageName;

    private static string NormalizeDisplayName(string? displayName, string packageName) =>
        string.IsNullOrWhiteSpace(displayName) ? packageName : displayName;
}

public sealed record AndroidAppLaunchEvent(
    AndroidAppLaunchStage Stage,
    AndroidAppLaunchIssueKind Issue,
    string Message,
    string? Detail,
    long TimestampUnixTimeMilliseconds);

public enum AndroidAppLaunchStage
{
    CommandReceived,
    PackageUnhidden,
    LaunchIntentResolved,
    StartActivityAttempted,
    StartActivityFailedWithException,
    TargetBecameForeground,
    PackageRehidden
}

public enum AndroidAppLaunchIssueKind
{
    None,
    QuietMode,
    MissingLauncherActivity,
    BackgroundActivityLaunchBlocked,
    HiddenOrSuspendedPackageState,
    UsageAccessDenied,
    WorkProfileUnavailable,
    DevicePolicyManagerUnavailable,
    PackageManagerUnavailable,
    InvalidRequest,
    StartActivityException
}
