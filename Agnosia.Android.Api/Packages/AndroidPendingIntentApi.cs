using System.Runtime.Versioning;
using Agnosia.Android.Api.Internal;
using Android.Content;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

public static class AndroidPendingIntentApi
{
    private const string LogTag = "AgnosiaPendingIntent";

    public static PendingIntent CreateWorkAppFrozenBroadcastPendingIntent(Context context, Type receiverType, string packageName)
    {
        var intent = new Intent(context, receiverType);
        intent.SetAction(AgnosiaActions.WorkAppFrozen);
        intent.PutExtra(AndroidProfileCommandGateway.ExtraTrigger, $"pending_intent_callback:{packageName}");
        AuthenticationUtility.SignWorkAppFrozenCallback(intent, packageName);

        Log.Debug(
            LogTag,
            $"Creating work-app frozen broadcast pending intent. package={packageName}.");

        return PendingIntent.GetBroadcast(
            context,
            GetStableRequestCode(AgnosiaActions.WorkAppFrozen, packageName),
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
            ?? throw new InvalidOperationException("Android could not create a PendingIntent for the work-app frozen callback.");
    }

    public static PendingIntent CreatePackageInstallerCallbackPendingIntent(Context context, Type receiverType, string action, int requestCode = 0)
    {
        var intent = new Intent(context, receiverType);
        intent.SetAction(action);

        return PendingIntent.GetBroadcast(
            context,
            requestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | GetPackageInstallerFlag())
            ?? throw new InvalidOperationException("Android could not create a PendingIntent for PackageInstaller.");
    }

    private static PendingIntentFlags GetPackageInstallerFlag() =>
        AndroidApiLevel.IsAtLeastS()
            ? PendingIntentFlags.Mutable
            : PendingIntentFlags.Immutable;

    public static Bundle? CreateSenderBackgroundActivityStartOptions()
    {
        if (!AndroidApiLevel.IsAtLeastUpsideDownCake())
        {
            return null;
        }

        var options = ActivityOptions.MakeBasic()
            ?? throw new InvalidOperationException("Android could not create ActivityOptions for sending the work-app frozen callback.");
        options.SetPendingIntentBackgroundActivityStartMode(GetAllowedBackgroundActivityStartMode());
        return options.ToBundle();
    }

    [SupportedOSPlatform("android34.0")]
    private static BackgroundActivityStartMode GetAllowedBackgroundActivityStartMode() =>
        OperatingSystem.IsAndroidVersionAtLeast(36)
            ? BackgroundActivityStartMode.AllowAlways
            : BackgroundActivityStartMode.Allowed;

    private static int GetStableRequestCode(string action, string packageName)
    {
        unchecked
        {
            var hash = 17;
            foreach (var symbol in action)
            {
                hash = hash * 31 + symbol;
            }

            foreach (var symbol in packageName)
            {
                hash = hash * 31 + symbol;
            }

            return hash & int.MaxValue;
        }
    }
}
