using System.Runtime.Versioning;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Internal;
using Agnosia.Android.Api.Platform;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Packages;

public static class AndroidPendingIntentApi
{
    private const string LogTag = "AgnosiaPendingIntent";

    public static PendingIntent CreateWorkAppFrozenBroadcastPendingIntent(Context context, Type receiverType,
        string packageName)
    {
        var intent = new Intent(context, receiverType);
        intent.SetAction(AgnosiaActions.WorkAppFrozen);
        intent.PutExtra(AndroidCommandContract.ExtraTrigger, $"pending_intent_callback:{packageName}");
        AuthenticationUtility.SignWorkAppFrozenCallback(intent, packageName);

        Log.Debug(
            LogTag,
            $"Creating work-app frozen broadcast pending intent. package={packageName}.");

        return PendingIntent.GetBroadcast(
                   context,
                   GetStableRequestCode(AgnosiaActions.WorkAppFrozen, packageName),
                   intent,
                   PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
               ?? throw new InvalidOperationException(
                   "Android could not create a PendingIntent for the work-app frozen callback.");
    }

    public static PendingIntent CreatePackageInstallerCallbackPendingIntent(
        Context context,
        Type receiverType,
        string action,
        string? packageName = null,
        string? operation = null)
    {
        var intent = new Intent(context, receiverType);
        intent.SetAction(action);
        if (!string.IsNullOrWhiteSpace(packageName))
            intent.PutExtra(AndroidCommandContract.ExtraCallbackPackage, packageName);

        if (!string.IsNullOrWhiteSpace(operation))
            intent.PutExtra(AndroidCommandContract.ExtraPackageInstallerOperation, operation);

        var requestCode = string.IsNullOrWhiteSpace(packageName) && string.IsNullOrWhiteSpace(operation)
            ? 0
            : GetStableRequestCode(action, $"{operation}:{packageName}");

        return PendingIntent.GetBroadcast(
                   context,
                   requestCode,
                   intent,
                   PendingIntentFlags.UpdateCurrent | GetPackageInstallerFlag())
               ?? throw new InvalidOperationException("Android could not create a PendingIntent for PackageInstaller.");
    }

    public static PendingIntent CreateBackgroundActivityStartPendingIntent(
        Context context,
        Intent intent,
        string action)
    {
        return PendingIntent.GetActivity(
                   context,
                   GetStableRequestCode(action, "background_activity_start"),
                   intent,
                   PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable,
                   CreateCreatorBackgroundActivityStartOptions())
               ?? throw new InvalidOperationException(
                   "Android could not create a PendingIntent for background activity start.");
    }

    private static PendingIntentFlags GetPackageInstallerFlag()
    {
        return AndroidApiLevel.IsAtLeastS()
            ? PendingIntentFlags.Mutable
            : PendingIntentFlags.Immutable;
    }

    public static Bundle? CreateSenderBackgroundActivityStartOptions()
    {
        if (!AndroidApiLevel.IsAtLeastUpsideDownCake()) return null;

        var options = ActivityOptions.MakeBasic()
                      ?? throw new InvalidOperationException(
                          "Android could not create ActivityOptions for sending the work-app frozen callback.");
        options.SetPendingIntentBackgroundActivityStartMode(GetAllowedBackgroundActivityStartMode());
        return options.ToBundle();
    }

    public static Bundle? CreateCreatorBackgroundActivityStartOptions()
    {
        if (!AndroidApiLevel.IsAtLeastUpsideDownCake()) return null;

        var options = ActivityOptions.MakeBasic()
                      ?? throw new InvalidOperationException(
                          "Android could not create ActivityOptions for creating a background activity PendingIntent.");
        options.SetPendingIntentCreatorBackgroundActivityStartMode(GetAllowedBackgroundActivityStartMode());
        return options.ToBundle();
    }

    [SupportedOSPlatform("android34.0")]
    private static BackgroundActivityStartMode GetAllowedBackgroundActivityStartMode()
    {
        return OperatingSystem.IsAndroidVersionAtLeast(36)
            ? BackgroundActivityStartMode.AllowAlways
            : BackgroundActivityStartMode.Allowed;
    }

    private static int GetStableRequestCode(string action, string packageName)
    {
        unchecked
        {
            var hash = action.Aggregate(17, (current, symbol) => current * 31 + symbol);
            hash = packageName.Aggregate(hash, (current, symbol) => current * 31 + symbol);
            return hash & int.MaxValue;
        }
    }
}
