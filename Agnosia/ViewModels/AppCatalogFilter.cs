using Agnosia.Models;

namespace Agnosia.ViewModels;

internal static class AppCatalogFilter
{
    public static AppItemViewModel[] FilterVisibleApps(
        AppItemViewModel[] personalApps,
        AppItemViewModel[] workApps,
        ProfileKind selectedProfile,
        string searchText)
    {
        var source = selectedProfile == ProfileKind.Work ? workApps : personalApps;
        var query = searchText.Trim();
        if (string.IsNullOrWhiteSpace(query)) return source;

        return source.Where(app =>
                app.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                app.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
