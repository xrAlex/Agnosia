using Agnosia.Models;
using Agnosia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public partial class AppPermissionsViewModel(IAppCommandService service, AppSnapshot app) : ObservableObject
{
    private IReadOnlyList<AppPermissionRowViewModel>? _visibleItems;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(VisibleItems), nameof(HasNoSearchResults), nameof(CountText))]
    public partial IReadOnlyList<AppPermissionRowViewModel> Items { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleItems), nameof(HasNoSearchResults))]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ChangePolicyCommand), nameof(RevokeCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; set; }

    public bool IsEmpty => HasLoaded && !IsBusy && !HasError && Items.Count == 0;

    public IReadOnlyList<AppPermissionRowViewModel> VisibleItems => _visibleItems ??= FilterItems();

    partial void OnItemsChanged(IReadOnlyList<AppPermissionRowViewModel> value) => _visibleItems = null;

    partial void OnSearchTextChanged(string value) => _visibleItems = null;

    private IReadOnlyList<AppPermissionRowViewModel> FilterItems()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        return query.Length == 0
            ? Items
            : Items.Where(item => item.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public bool HasNoSearchResults => Items.Count > 0 && VisibleItems.Count == 0;

    public string CountText => $"Всего: {Items.Count} · Запрещено: {Items.Count(item => item.Snapshot.State == AppPermissionState.PolicyDenied)}";

    public string Explanation => app.Profile == ProfileKind.Work
        ? "«Отозвать» снимает выданный доступ без постоянного запрета. «Запретить» снимает доступ и блокирует повторные запросы. «Снять запрет» снова разрешает запросить доступ, но не выдаёт его. Некоторые разрешения Android не позволяет менять."
        : "Разрешения личного приложения доступны для просмотра. Управление запретами работает в рабочем профиле.";

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Message = string.Empty;
        HasError = false;
        try { await ReadAsync(); }
        finally { IsBusy = false; }
    }

    private bool CanChangePolicy(AppPermissionRowViewModel? row) =>
        !IsBusy && app.Profile == ProfileKind.Work && row is { CanChangePolicy: true } && Items.Contains(row);

    [RelayCommand(CanExecute = nameof(CanChangePolicy))]
    private async Task ChangePolicyAsync(AppPermissionRowViewModel? row)
    {
        if (!CanChangePolicy(row)) return;
        await ApplyAsync(() => service.SetAppPermissionDeniedAsync(app, row!.Name,
            row.Snapshot.State != AppPermissionState.PolicyDenied));
    }

    private bool CanRevoke(AppPermissionRowViewModel? row) =>
        !IsBusy && app.Profile == ProfileKind.Work && row is { CanRevoke: true } && Items.Contains(row);

    [RelayCommand(CanExecute = nameof(CanRevoke))]
    private async Task RevokeAsync(AppPermissionRowViewModel? row)
    {
        if (!CanRevoke(row)) return;
        await ApplyAsync(() => service.RevokeAppPermissionAsync(app, row!.Name));
    }

    private async Task ApplyAsync(Func<Task<OperationResult>> operation)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            var result = await operation();
            Message = result.Message;
            HasError = !result.Succeeded;
        }
        catch (Exception exception)
        {
            Message = exception.Message;
            HasError = true;
        }
        finally
        {
            // Re-read even after failure: Android may have applied part of the operation.
            await ReadAsync();
            IsBusy = false;
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            var permissions = await service.LoadAppPermissionsAsync(app);
            Items = permissions.Select(permission => new AppPermissionRowViewModel(permission,
                    app.Profile == ProfileKind.Work, ChangePolicyCommand, RevokeCommand))
                .OrderBy(row => row.Snapshot.Kind switch
                {
                    AppPermissionKind.Runtime => 0,
                    AppPermissionKind.Special => 1,
                    AppPermissionKind.Manifest => 2,
                    _ => 3
                })
                .ThenBy(row => row.Snapshot.State is AppPermissionState.Granted or AppPermissionState.PolicyGranted ? 0 : 1)
                .ThenBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Name, StringComparer.Ordinal).ToArray();
            HasLoaded = true;
        }
        catch (Exception exception)
        {
            Items = [];
            HasError = true;
            Message = $"Не удалось обновить разрешения. {exception.Message}";
        }
        ChangePolicyCommand.NotifyCanExecuteChanged();
        RevokeCommand.NotifyCanExecuteChanged();
    }
}

public sealed class AppPermissionRowViewModel(
    AppPermissionSnapshot snapshot, bool isWorkProfile, IAsyncRelayCommand<AppPermissionRowViewModel?> command,
    IAsyncRelayCommand<AppPermissionRowViewModel?> revokeCommand)
{
    private readonly AppPermissionText? permissionText = AppPermissionDictionary.Find(snapshot.Name);

    public AppPermissionSnapshot Snapshot => snapshot;
    public string Name => snapshot.Name;
    public string Label => permissionText?.Label ?? AppPermissionDictionary.UnknownLabel;
    public string? Description => permissionText?.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool CanChangePolicy => isWorkProfile && snapshot.CanChangePolicy;
    public bool CanRevoke => isWorkProfile && snapshot.Kind == AppPermissionKind.Runtime
                             && snapshot.CanChangePolicy && snapshot.CanRevokeGrant
                             && (snapshot.State is AppPermissionState.Granted or AppPermissionState.PolicyGranted);
    public string? RestrictionReason => snapshot.RestrictionReason;
    public bool HasRestriction => !string.IsNullOrWhiteSpace(RestrictionReason);
    public IAsyncRelayCommand<AppPermissionRowViewModel?> ChangePolicyCommand => command;
    public IAsyncRelayCommand<AppPermissionRowViewModel?> RevokeCommand => revokeCommand;
    public string ActionText => snapshot.State == AppPermissionState.PolicyDenied ? "Снять запрет" : "Запретить";
    public string ActionAutomationName => $"{ActionText}: {Label}";
    public string RevokeAutomationName => $"Отозвать: {Label}";
    public string KindText => snapshot.Kind switch
    {
        AppPermissionKind.Runtime => "Запрашивается во время работы",
        AppPermissionKind.Manifest => "Указано при установке",
        AppPermissionKind.Special => "Специальный доступ Android",
        _ => "Тип разрешения неизвестен"
    };
    public string StatusText => snapshot.State switch
    {
        AppPermissionState.Granted => CanRevoke ? "Выдано" : "Выдано · нельзя отозвать",
        AppPermissionState.NotGranted => "Не выдано",
        AppPermissionState.PolicyDenied => "Запрещено",
        AppPermissionState.PolicyGranted => CanRevoke ? "Выдано политикой" : "Выдано политикой · нельзя отозвать",
        _ => "Статус недоступен"
    };
}
