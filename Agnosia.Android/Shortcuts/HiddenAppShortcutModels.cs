namespace Agnosia.Android.Shortcuts;

internal sealed record HiddenAppLaunchRequest(
    string PackageName,
    string? TargetActivity,
    string DisplayName)
{
    public static HiddenAppLaunchRequest Empty { get; } = new(string.Empty, null, string.Empty);
}

internal sealed record ShortcutFreezePreparationResult(
    bool Succeeded,
    bool HideImmediately,
    string Message)
{
    public static ShortcutFreezePreparationResult Failure(string message)
    {
        return new ShortcutFreezePreparationResult(false, false, message);
    }

    public static ShortcutFreezePreparationResult Immediate(string message)
    {
        return new ShortcutFreezePreparationResult(true, true, message);
    }

    public static ShortcutFreezePreparationResult Deferred(string message)
    {
        return new ShortcutFreezePreparationResult(true, false, message);
    }
}

internal sealed record HiddenAppShortcutBuildResult(
    bool Succeeded,
    HiddenAppShortcutMetadata Metadata,
    string Error)
{
    public static HiddenAppShortcutBuildResult Success(HiddenAppShortcutMetadata metadata)
    {
        return new HiddenAppShortcutBuildResult(true, metadata, string.Empty);
    }

    public static HiddenAppShortcutBuildResult Failure(string error)
    {
        return new HiddenAppShortcutBuildResult(false, HiddenAppShortcutMetadata.Empty, error);
    }
}

internal sealed record HiddenAppShortcutMetadata(
    string ShortcutId,
    string TargetPackage,
    string? TargetActivity,
    string Label,
    string IconBase64,
    string Token)
{
    public static HiddenAppShortcutMetadata Empty { get; } =
        new(string.Empty, string.Empty, null, string.Empty, string.Empty, string.Empty);
}
