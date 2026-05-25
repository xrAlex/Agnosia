namespace Agnosia.Models;

public readonly record struct AppItemKey(ProfileKind Profile, string PackageName)
{
    public static AppItemKey FromSnapshot(AppSnapshot snapshot)
    {
        return new AppItemKey(snapshot.Profile, snapshot.PackageName);
    }
}
