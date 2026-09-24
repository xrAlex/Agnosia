using System.Diagnostics;
using System.Text.Json;
using Agnosia.Android.Commands.Handlers;

namespace Agnosia.Android.Commands.Transports;

internal sealed class ProviderCommandTransport(CommandAccessCoordinator accessCoordinator, ProviderCommandClient client)
    : IAndroidCommandTransport
{
    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.Provider;

    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(AndroidCommandEnvelope envelope, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!ProviderCommandPolicy.Supports(envelope.Kind))
            return Failure(envelope, "unsupported_command", stopwatch);
        var access = await accessCoordinator.GetAccessAsync(cancellationToken).ConfigureAwait(false);
        if (!access.Ready) return Failure(envelope, access.Error ?? "grant_missing", stopwatch);
        if (envelope.Kind == AndroidCommandKind.QueryLogs)
            return await ReadLogsAsync(envelope, access.Access!, cancellationToken).ConfigureAwait(false);
        if (envelope.Kind == AndroidCommandKind.QueryAppIcons)
        {
            var query = JsonSerializer.Deserialize<QueryAppIconsRequest>(envelope.PayloadJson ?? "null");
            var names = query?.PackageNames?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
            var icons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            foreach (var chunk in names.Chunk(ProviderCommandProtocol.MaxIcons))
            {
                var result = await ReadIconsAsync(envelope, access.Access!, chunk, icons, cancellationToken).ConfigureAwait(false);
                if (result is not null) return result;
            }
            return AndroidCommandResultEnvelope.Success(envelope.CorrelationId, envelope.Kind, Kind,
                JsonSerializer.Serialize(new QueryAppIconsResponse(icons)), "Icons loaded.", stopwatch.Elapsed, "provider; batched");
        }
        return await SendAsync(envelope, access.Access!, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AndroidCommandResultEnvelope> ReadLogsAsync(AndroidCommandEnvelope envelope, CommandProviderAccess access,
        CancellationToken token)
    {
        var logs = new List<Agnosia.Models.AppLogEntry>();
        var pageToken = Guid.NewGuid().ToString("N");
        var offset = 0;
        for (var index = 0; index < 100; index++)
        {
            var request = envelope with { CorrelationId = Guid.NewGuid(),
                PayloadJson = JsonSerializer.Serialize(new ProviderLogPageRequest(pageToken, offset)) };
            var result = await SendAsync(request, access, token).ConfigureAwait(false);
            if (!result.Succeeded) return result with { CorrelationId = envelope.CorrelationId };
            var page = JsonSerializer.Deserialize<ProviderLogPageResponse>(result.PayloadJson ?? "null");
            if (page is null || (page.HasMore && page.NextOffset <= offset))
                return Failure(envelope, "invalid_response", Stopwatch.StartNew());
            logs.AddRange(JsonSerializer.Deserialize(page.LogsJson, AndroidApiJsonContext.Default.ListAppLogEntry) ?? []);
            if (!page.HasMore)
                return result with { CorrelationId = envelope.CorrelationId, PayloadJson = JsonSerializer.Serialize(
                    new ProviderLogPageResponse(JsonSerializer.Serialize(logs, AndroidApiJsonContext.Default.ListAppLogEntry), page.NextOffset, false)) };
            offset = page.NextOffset;
        }
        return Failure(envelope, "payload_too_large", Stopwatch.StartNew());
    }

    private async Task<AndroidCommandResultEnvelope?> ReadIconsAsync(AndroidCommandEnvelope envelope, CommandProviderAccess access,
        string[] packages, Dictionary<string, byte[]?> icons, CancellationToken token)
    {
        var part = envelope with { CorrelationId = Guid.NewGuid(), PayloadJson = JsonSerializer.Serialize(new QueryAppIconsRequest(packages)) };
        var result = await SendAsync(part, access, token).ConfigureAwait(false);
        if (result.ErrorCode == "payload_too_large" && packages.Length > 1)
        {
            var middle = packages.Length / 2;
            return await ReadIconsAsync(envelope, access, packages[..middle], icons, token).ConfigureAwait(false)
                   ?? await ReadIconsAsync(envelope, access, packages[middle..], icons, token).ConfigureAwait(false);
        }
        if (!result.Succeeded) return result with { CorrelationId = envelope.CorrelationId };
        var response = JsonSerializer.Deserialize<QueryAppIconsResponse>(result.PayloadJson ?? "null");
        if (response?.Icons is not null)
            foreach (var (package, bytes) in response.Icons) icons[package] = bytes;
        return null;
    }

    private async Task<AndroidCommandResultEnvelope> SendAsync(AndroidCommandEnvelope envelope, CommandProviderAccess access,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var request = new ProviderCommandMessage(ProviderCommandProtocol.Version, envelope.CorrelationId, envelope.Kind.ToString(),
            ProviderProfileIdentity.CurrentUserId, access.UserId, access.Generation, now,
            now + (long)Math.Clamp(envelope.Timeout.TotalMilliseconds, 1, 120_000), false, false, envelope.PayloadJson, null, "", "");
        var dispatch = new CommandDispatchState();
        try
        {
            var response = await client.CallAsync(request, token, dispatch).ConfigureAwait(false);
            if (response.ErrorCode is "wrong_profile" or "profile_unavailable") accessCoordinator.Invalidate(response.ErrorCode);
            return new AndroidCommandResultEnvelope(envelope.CorrelationId, envelope.Kind, response.Succeeded, Kind,
                response.PayloadJson, response.Message, response.ErrorCode, stopwatch.Elapsed, "provider; authenticated");
        }
        catch (ProviderAuthenticationException)
        {
            return Failure(envelope, ProviderCommandPolicy.IsMutation(envelope.Kind) && dispatch.MayHaveDispatched
                ? "outcome_unknown" : "authentication_failed", stopwatch);
        }
        catch (OperationCanceledException) when (!dispatch.MayHaveDispatched || !ProviderCommandPolicy.IsMutation(envelope.Kind)) { throw; }
        catch (Exception exception)
        {
            if (ProviderCommandPolicy.IsMutation(envelope.Kind) && dispatch.MayHaveDispatched)
                return Failure(envelope, "outcome_unknown", stopwatch);
            if (exception is InvalidDataException) return Failure(envelope, "payload_too_large", stopwatch);
            if (exception is Java.Lang.SecurityException) return Failure(envelope, "authentication_failed", stopwatch);
            accessCoordinator.Invalidate("profile_unavailable");
            return Failure(envelope, "profile_unavailable", stopwatch);
        }
    }

    private AndroidCommandResultEnvelope Failure(AndroidCommandEnvelope envelope, string error, Stopwatch stopwatch) =>
        AndroidCommandResultEnvelope.Failure(envelope.CorrelationId, envelope.Kind, Kind,
            error switch
            {
                "outcome_unknown" => "Результат операции неизвестен. Обновите состояние приложения.",
                "profile_unavailable" => "Включите и разблокируйте рабочий профиль, затем обновите состояние.",
                "grant_missing" => "Не удалось подключиться к рабочему профилю. Нажмите «Обновить», чтобы повторить подключение.",
                "authentication_failed" or "incompatible_version" => "Не удалось проверить командный канал. Убедитесь, что Agnosia обновлена в обоих профилях.",
                "payload_too_large" => "Ответ рабочего профиля слишком большой. Не удалось завершить загрузку.",
                "unsupported_command" => "Эта команда не поддерживается Provider. Выберите Авто или Activity в настройках.",
                _ => "Рабочий профиль не выполнил команду. Попробуйте обновить состояние."
            }, error, stopwatch.Elapsed, "provider");
}
