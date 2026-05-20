using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class AppCatalogFilterTests
{
    private readonly DashboardWorkspaceViewModel _owner = TestWorkspaceFactory.Create();

    // Проверяет, что пустой поиск возвращает исходный список выбранного профиля.
    [Fact]
    public void FilterVisibleApps_returns_selected_profile_source_when_search_is_blank()
    {
        var personalApps = new[] { App(ProfileKind.Personal, "com.example.personal", "Personal") };
        var workApps = new[] { App(ProfileKind.Work, "com.example.work", "Work") };

        var personalResult = AppCatalogFilter.FilterVisibleApps(personalApps, workApps, ProfileKind.Personal, "  ");
        var workResult = AppCatalogFilter.FilterVisibleApps(personalApps, workApps, ProfileKind.Work, string.Empty);

        Assert.Same(personalApps, personalResult);
        Assert.Same(workApps, workResult);
    }

    // Проверяет поиск по названию приложения только внутри выбранного профиля.
    [Fact]
    public void FilterVisibleApps_searches_label_in_selected_profile_case_insensitively()
    {
        var personalApps = new[]
        {
            App(ProfileKind.Personal, "com.example.notes", "Personal Notes")
        };
        var workApps = new[]
        {
            App(ProfileKind.Work, "com.example.mail", "Work Mail"),
            App(ProfileKind.Work, "com.example.notes", "Work Notes")
        };

        var result = AppCatalogFilter.FilterVisibleApps(personalApps, workApps, ProfileKind.Work, "notes");

        var match = Assert.Single(result);
        Assert.Same(workApps[1], match);
    }

    // Проверяет регистронезависимый поиск по package name.
    [Fact]
    public void FilterVisibleApps_searches_package_name_ordinal_ignore_case()
    {
        var personalApps = new[]
        {
            App(ProfileKind.Personal, "com.example.alpha", "Alpha"),
            App(ProfileKind.Personal, "org.example.beta", "Beta")
        };
        var workApps = new[]
        {
            App(ProfileKind.Work, "com.example.alpha", "Work Alpha")
        };

        var result = AppCatalogFilter.FilterVisibleApps(personalApps, workApps, ProfileKind.Personal, "ORG.EXAMPLE");

        var match = Assert.Single(result);
        Assert.Same(personalApps[1], match);
    }

    // Проверяет, что пробелы вокруг поискового запроса не мешают фильтрации.
    [Fact]
    public void FilterVisibleApps_trims_search_text_before_matching()
    {
        var personalApps = new[]
        {
            App(ProfileKind.Personal, "com.example.alpha", "Alpha"),
            App(ProfileKind.Personal, "com.example.beta", "Beta")
        };

        var result = AppCatalogFilter.FilterVisibleApps(personalApps, [], ProfileKind.Personal, "  alpha  ");

        var match = Assert.Single(result);
        Assert.Same(personalApps[0], match);
    }

    private AppItemViewModel App(ProfileKind profile, string packageName, string label)
    {
        return TestWorkspaceFactory.CreateApp(_owner, profile, packageName, label);
    }
}
