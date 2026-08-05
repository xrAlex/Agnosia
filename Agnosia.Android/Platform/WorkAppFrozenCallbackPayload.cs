namespace Agnosia.Android.Platform;

internal static class WorkAppFrozenCallbackPayload
{
    private const string CurrentVersion = "AGNOSIA_WORK_APP_FROZEN_CALLBACK_2";
    private const string LegacyVersion = "AGNOSIA_WORK_APP_FROZEN_CALLBACK_1";

    public static string Create(string packageName, string launchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        return CurrentVersion + "\n" + packageName + "\n" + launchId;
    }

    public static string CreateLegacy(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return LegacyVersion + "\n" + packageName;
    }
}
