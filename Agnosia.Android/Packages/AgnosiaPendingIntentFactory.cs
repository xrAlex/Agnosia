using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Packages;

internal static class AgnosiaPendingIntentFactory
{
    private const string LogTag = "AgnosiaPendingIntent";

    public static PendingIntent CreateWorkAppFrozenBroadcastPendingIntent(
        Context context,
        Type receiverType,
        string packageName,
        string launchId)
    {
        var intent = new Intent(context, receiverType);
        intent.SetAction(AgnosiaActions.WorkAppFrozen);
        intent.PutExtra(AndroidCommandContract.ExtraTrigger, $"pending_intent_callback:{packageName}");
        AuthenticationUtility.SignWorkAppFrozenCallback(intent, packageName, launchId);

        Log.Debug(
            LogTag,
            $"Creating work-app frozen broadcast pending intent. package={packageName}, launchId={launchId}.");

        return PendingIntent.GetBroadcast(
                   context,
                   GetStableRequestCode(AgnosiaActions.WorkAppFrozen, packageName, launchId),
                   intent,
                   PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
               ?? throw new InvalidOperationException(
                   "Android could not create a PendingIntent for the work-app frozen callback.");
    }

    private static int GetStableRequestCode(string action, string packageName, string launchId)
    {
        unchecked
        {
            var hash = action.Aggregate(17, (current, symbol) => current * 31 + symbol);
            hash = packageName.Aggregate(hash, (current, symbol) => current * 31 + symbol);
            hash = launchId.Aggregate(hash, (current, symbol) => current * 31 + symbol);
            return hash & int.MaxValue;
        }
    }
}
