using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class AppItemViewModelTests
{
    // Проверяет доступные действия для обычного личного приложения.
    [Fact]
    public void Personal_non_system_app_exposes_move_and_uninstall_controls()
    {
        var app = CreateApp(TestSnapshots.App(ProfileKind.Personal, isSystem: false));

        Assert.Equal("Personal", app.ProfileLabel);
        Assert.True(app.CanClone);
        Assert.True(app.CanMoveToWork);
        Assert.True(app.CanUninstall);
        Assert.False(app.ShowWorkControls);
        Assert.False(app.HasStatusTag);
        Assert.Equal("Open", app.LaunchLabel);
        Assert.Equal("CopyToWork", app.CloneLabel);
        Assert.Equal("DisallowInteraction", app.InteractionLabel);
    }

    // Проверяет состояние скрытого системного приложения в рабочем профиле.
    [Fact]
    public void Work_hidden_system_app_exposes_isolation_state_without_mutating_controls()
    {
        var app = CreateApp(TestSnapshots.App(
            ProfileKind.Work,
            isSystem: true,
            isHidden: true,
            canLaunch: false));

        Assert.Equal("Work", app.ProfileLabel);
        Assert.True(app.ShowWorkControls);
        Assert.True(app.CanFreeze);
        Assert.True(app.IsAgnosiaManaged);
        Assert.True(app.HasStatusTag);
        Assert.True(app.ShowSecondaryRow);
        Assert.False(app.CanClone);
        Assert.False(app.CanUninstall);
        Assert.True(app.ShowLaunch);
        Assert.Equal("Isolated", app.StatusTagLabel);
        Assert.Equal("UnfreezeAndOpen", app.LaunchLabel);
        Assert.Equal("CopyToPersonal", app.CloneLabel);
    }

    // Проверяет обновление вычисляемых свойств и PropertyChanged при новом snapshot.
    [Fact]
    public void ApplySnapshot_updates_derived_properties_and_raises_change_notifications()
    {
        var app = CreateApp(TestSnapshots.App(ProfileKind.Personal, label: "Alpha"));
        var changedProperties = new List<string?>();
        app.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        app.ApplySnapshot(TestSnapshots.App(
            ProfileKind.Personal,
            label: "Beta",
            isSystem: true,
            canLaunch: false,
            isInstalled: false,
            interactionAllowed: false));

        Assert.Equal("Beta", app.Label);
        Assert.Equal("B", app.Monogram);
        Assert.True(app.CanClone);
        Assert.False(app.CanMoveToWork);
        Assert.False(app.CanUninstall);
        Assert.False(app.ShowLaunch);
        Assert.Equal("NotInstalled", app.StatusTagLabel);
        Assert.Equal("AllowInteraction", app.InteractionLabel);
        Assert.Contains(nameof(AppItemViewModel.Label), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.Monogram), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.CanMoveToWork), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.CanUninstall), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.ShowLaunch), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.StatusTagLabel), changedProperties);
        Assert.Contains(nameof(AppItemViewModel.InteractionLabel), changedProperties);
    }

    // Проверяет запрет смены идентичности app item через ApplySnapshot.
    [Fact]
    public void ApplySnapshot_rejects_identity_changes()
    {
        var app = CreateApp(TestSnapshots.App(ProfileKind.Personal, packageName: "com.example.alpha"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            app.ApplySnapshot(TestSnapshots.App(ProfileKind.Personal, packageName: "com.example.beta")));

        Assert.Equal("App item identity cannot be changed.", exception.Message);
    }

    private static AppItemViewModel CreateApp(AppSnapshot snapshot)
    {
        return TestWorkspaceFactory.CreateApp(TestWorkspaceFactory.Create(), snapshot);
    }
}
