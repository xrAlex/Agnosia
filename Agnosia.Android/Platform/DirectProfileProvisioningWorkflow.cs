using Agnosia.Models;

namespace Agnosia.Android.Platform;

internal sealed class DirectProfileProvisioningWorkflow(
    IRootCommandRunner runner,
    IDirectProfileProvisioningStore store,
    string packageName,
    string adminComponent,
    Func<DirectProfileUser, CancellationToken, Task<bool>> confirmProfile)
{
    public async Task<OperationResult> RunAsync(int parentId, CancellationToken cancellationToken = default)
    {
        var step = "получение root-доступа";
        var mutationInFlight = false;
        try
        {
            var root = await runner.RunAsync("id -u", cancellationToken).ConfigureAwait(false);
            if (root.ExitCode != 0 || root.Output.Trim() != "0")
                return OperationResult.Failure("Root-доступ не предоставлен. Разрешите Agnosia доступ через su или используйте обычное создание профиля.");

            step = "проверка профилей Android";
            var users = await ReadUsersAsync(cancellationToken).ConfigureAwait(false);
            var parent = users.SingleOrDefault(user => user.Id == parentId);
            if (parent is null || parent.IsManaged || parent.ParentId is not null || !parent.IsUsable)
                throw new ProvisioningFailure("Создание через root нужно запускать из личного профиля.");

            var state = store.State;
            var associated = users.Where(user => user.IsManaged && user.ParentId == parentId).ToArray();
            DirectProfileUser? profile = null;
            if (state is not null)
            {
                if (state.RequiresReboot(store.BootCount))
                    throw new ProvisioningFailure("Результат предыдущей root-команды не подтверждён. " +
                                                  "Перезагрузите устройство перед повторной попыткой.");
                if (state.ParentId != parentId || state.ParentSerial != parent.Serial)
                    throw Recovery("Сохранённая настройка относится к другому пользователю Android.");
                profile = users.SingleOrDefault(user => user.Id == state.UserId);
                if (profile is not null && (!profile.IsManaged || profile.ParentId != parentId
                                           || profile.Serial != state.UserSerial || !profile.IsUsable))
                    throw Recovery("Идентификатор сохранённого профиля изменился.");
                if (profile is not null && state.Stage == DirectProfileProvisioningStage.Complete)
                    throw new ProvisioningFailure("Рабочий профиль уже настроен. Включите его в настройках Android и проверьте связь с Agnosia.");
            }

            if (associated.Any(user => user.Id != profile?.Id))
                throw Recovery("В личном профиле уже есть рабочий профиль, который не относится к этой настройке.");

            // Verify shell support and ownership before creating anything or changing authentication.
            var owners = await ReadOwnersAsync(cancellationToken).ConfigureAwait(false);
            if (profile is not null && owners.TryGetValue(profile.Id, out var existingOwner) && existingOwner != adminComponent)
                throw Recovery("Рабочим профилем управляет другое приложение.");

            string key;
            if (profile is null)
            {
                key = AuthenticationKeyMaterial.Create();
                state = new(parentId, parent.Serial, -1, -1, DirectProfileProvisioningStage.CreatingProfile);
                store.Save(state, key);
                step = "создание рабочего профиля";
                var created = await RunMutationAsync(DirectProfileProvisioningCommands.CreateUser(parentId)).ConfigureAwait(false);
                var userId = DirectProfileProvisioningCommands.ParseCreatedUserId(created)
                             ?? throw Recovery("Android не подтвердил идентификатор созданного профиля.");
                // Persist the returned ID immediately; a missing serial must never authorize adopting an unrelated user.
                state = state with { UserId = userId };
                store.Save(state, key);
                users = await ReadUsersAsync(cancellationToken).ConfigureAwait(false);
                profile = users.SingleOrDefault(user => user.Id == userId && user.IsManaged
                                                       && user.ParentId == parentId && user.IsUsable)
                          ?? throw Recovery("Android не подтвердил создание управляемого профиля.");
                state = state with { UserSerial = profile.Serial };
                store.Save(state, key);
            }
            else
            {
                key = store.Key ?? "";
                if (!AuthenticationKeyMaterial.IsValid(key))
                    throw Recovery("Не найден ключ связи с незавершённым рабочим профилем.");
            }

            step = "установка Agnosia в рабочий профиль";
            SaveStage(DirectProfileProvisioningStage.InstallingPackage);
            await RunMutationAsync(DirectProfileProvisioningCommands.InstallExisting(profile.Id, packageName)).ConfigureAwait(false);
            var paths = await RunAsync(DirectProfileProvisioningCommands.PackagePath(profile.Id, packageName), cancellationToken).ConfigureAwait(false);
            if (!paths.Split('\n').Any(line => line.StartsWith("package:/", StringComparison.Ordinal)))
                throw Recovery("Android не подтвердил установку Agnosia в рабочем профиле.");

            step = "назначение владельца профиля";
            SaveStage(DirectProfileProvisioningStage.AssigningOwner);
            owners = await ReadOwnersAsync(cancellationToken).ConfigureAwait(false);
            if (owners.TryGetValue(profile.Id, out var owner))
            {
                if (owner != adminComponent) throw Recovery("Рабочим профилем управляет другое приложение.");
            }
            else
            {
                await RunMutationAsync(DirectProfileProvisioningCommands.SetOwner(profile.Id, adminComponent)).ConfigureAwait(false);
                owners = await ReadOwnersAsync(cancellationToken).ConfigureAwait(false);
                if (!owners.TryGetValue(profile.Id, out owner) || owner != adminComponent)
                    throw Recovery("Android не подтвердил Agnosia как владельца рабочего профиля.");
            }

            step = "запуск рабочего профиля";
            SaveStage(DirectProfileProvisioningStage.StartingProfile);
            var started = await RunMutationAsync(DirectProfileProvisioningCommands.StartUser(profile.Id)).ConfigureAwait(false);
            if (started.Trim() != "Success: user started") throw Recovery("Android не подтвердил запуск рабочего профиля.");

            step = "настройка рабочего профиля";
            SaveStage(DirectProfileProvisioningStage.ApplyingPolicies);
            var setup = await RunMutationAsync(DirectProfileProvisioningCommands.Setup(profile.Id, packageName, key)).ConfigureAwait(false);
            if (!DirectProfileProvisioningCommands.IsSetupAcknowledged(setup))
                throw Recovery("Рабочая копия Agnosia не подтвердила применение настроек.");

            step = "проверка связи с рабочим профилем";
            SaveStage(DirectProfileProvisioningStage.VerifyingConnection);
            if (!await confirmProfile(profile, cancellationToken).ConfigureAwait(false))
                throw Recovery("Не удалось подтвердить защищённую связь и владельца рабочего профиля.");
            SaveStage(DirectProfileProvisioningStage.Complete);
            return OperationResult.Success("Рабочий профиль подключен.");

            void SaveStage(DirectProfileProvisioningStage stage)
            {
                state = state! with { Stage = stage };
                store.Save(state, key);
            }

            async Task<string> RunMutationAsync(string command)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var confirmedStage = state!.Stage;
                // Persist before submission: process death must not allow a new app instance to race
                // a still-running command in the root daemon. A confirmed result clears this barrier.
                state = state with { Stage = DirectProfileProvisioningStage.AwaitingReboot,
                    UncertainBootCount = store.BootCount >= 0 ? store.BootCount : null };
                store.Save(state, key);
                mutationInFlight = true;
                var result = await runner.RunAsync(command, cancellationToken).ConfigureAwait(false);
                mutationInFlight = false;
                state = state with { Stage = confirmedStage, UncertainBootCount = null };
                store.Save(state, key);
                if (result.ExitCode != 0) throw new IOException("Root command failed.");
                return result.Output;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (RootCommandOutcomeUnknownException) when (mutationInFlight)
        {
            if (store.State is { } pending && store.Key is { } key)
                store.Save(pending with { Stage = DirectProfileProvisioningStage.AwaitingReboot,
                    UncertainBootCount = store.BootCount >= 0 ? store.BootCount : null }, key);
            return OperationResult.Failure($"Не подтверждено завершение этапа «{step}». " +
                                           "Перезагрузите устройство перед повторной попыткой создания через root.");
        }
        catch (ProvisioningFailure exception) { return OperationResult.Failure(exception.Message); }
        catch (Exception exception) when (exception is IOException or FormatException or OverflowException or TimeoutException)
        {
            // Never expose shell output: the setup command carries authentication material.
            return OperationResult.Failure($"Не удалось выполнить этап «{step}». " +
                                           "Проверьте root-доступ и повторите создание через root. " +
                                           "Если профиль остался недоступным, откройте настройки рабочего профиля Android.");
        }
    }

    private async Task<string> RunAsync(string command, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException("Root command failed.");
        return result.Output;
    }

    private async Task<IReadOnlyList<DirectProfileUser>> ReadUsersAsync(CancellationToken cancellationToken) =>
        DirectProfileProvisioningCommands.ParseUsers(await RunAsync("dumpsys user", cancellationToken).ConfigureAwait(false));

    private async Task<IReadOnlyDictionary<int, string>> ReadOwnersAsync(CancellationToken cancellationToken) =>
        DirectProfileProvisioningCommands.ParseOwners(await RunAsync("dpm list-owners", cancellationToken).ConfigureAwait(false));

    private static ProvisioningFailure Recovery(string message) => new(message +
        " Повторите создание через root для продолжения собственной настройки. " +
        "Если это не помогает, удалите рабочий профиль в настройках Android и начните заново.");

    private sealed class ProvisioningFailure(string message) : Exception(message);
}
