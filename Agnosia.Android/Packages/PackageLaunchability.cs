namespace Agnosia.Android.Packages;

internal interface IPackageLaunchQuery
{
    bool HasDirectLaunchIntent();

    bool HasInfoActivity();

    bool HasLauncherActivity();
}

internal static class PackageLaunchability
{
    public static bool CanLaunch(bool packageAvailable, IPackageLaunchQuery query)
    {
        return packageAvailable
               && (query.HasDirectLaunchIntent()
               || query.HasInfoActivity()
               || query.HasLauncherActivity());
    }
}
