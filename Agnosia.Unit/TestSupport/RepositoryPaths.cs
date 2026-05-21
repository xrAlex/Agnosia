namespace Agnosia.Unit.TestSupport;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRepositoryRoot();

    public static string Get(params string[] segments)
    {
        var parts = new string[segments.Length + 1];
        parts[0] = Root;
        segments.CopyTo(parts, 1);
        return Path.Combine(parts);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Agnosia.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Agnosia repository root.");
    }
}
