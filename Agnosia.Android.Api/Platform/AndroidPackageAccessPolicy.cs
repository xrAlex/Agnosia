namespace Agnosia.Android.Api.Platform;

public static class AndroidPackageAccessPolicy
{
    private static readonly AndroidPackageAccessRule[] Rules =
    [
        new("ru.sberbankmobile", true)
    ];

    public static bool RequiresCrossProfileInteraction(string? packageName)
    {
        return TryGetRule(packageName, out var rule) && rule.AccessControlDisabled;
    }

    public static string[] ApplyRequiredCrossProfilePackages(IEnumerable<string>? packageNames)
    {
        var merged = new HashSet<string>(
            packageNames?.Where(packageName => !string.IsNullOrWhiteSpace(packageName)) ?? [],
            StringComparer.Ordinal);

        foreach (var rule in Rules)
            if (rule.AccessControlDisabled)
                merged.Add(rule.PackageName);

        return merged.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool TryGetRule(string? packageName, out AndroidPackageAccessRule rule)
    {
        if (!string.IsNullOrWhiteSpace(packageName))
            foreach (var candidate in Rules)
                if (string.Equals(candidate.PackageName, packageName, StringComparison.Ordinal))
                {
                    rule = candidate;
                    return true;
                }

        rule = default;
        return false;
    }

    private readonly record struct AndroidPackageAccessRule(
        string PackageName,
        bool AccessControlDisabled);
}