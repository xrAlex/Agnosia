using Android.Content;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

public static class AndroidIntentApi
{
    public static bool TryStartActivity(
        Context context,
        Intent intent,
        string logTag,
        string errorMessage,
        out string? error)
    {
        try
        {
            context.StartActivity(intent);
            error = null;
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"{errorMessage} Details: {exception}");
            error = errorMessage;
            return false;
        }
    }

    public static bool TryTransferToProfileAndStartActivity(
        Context context,
        Intent intent,
        string logTag,
        string errorMessage,
        out string? error)
    {
        try
        {
            AgnosiaUtilities.TransferIntentToProfile(context, intent);
            context.StartActivity(intent);
            error = null;
            return true;
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(logTag, $"{errorMessage} Details: {exception}");
            error = errorMessage;
            return false;
        }
    }
}
