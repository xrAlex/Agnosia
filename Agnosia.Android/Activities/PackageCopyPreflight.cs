namespace Agnosia.Android.Activities;

internal static class PackageCopyPreflight
{
    public static bool CanReuseExistingPackage(
        bool requested,
        bool isWorkProfileOwner,
        bool? installed,
        long? sourceVersionCode,
        long? workVersionCode)
        => requested && isWorkProfileOwner && installed == true
           && sourceVersionCode is >= 0 && workVersionCode is >= 0
           && workVersionCode.Value >= sourceVersionCode.Value;
}
