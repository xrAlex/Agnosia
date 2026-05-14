using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Platform;

public static class AndroidServiceApi
{
    public static bool TryStartForegroundService(Context context, Intent intent, string logTag, string errorMessage)
    {
        try
        {
            context.StartForegroundService(intent);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn(logTag, $"{errorMessage} Details: {exception}");
            return false;
        }
    }

    public static bool TryStartService(Context context, Intent intent, string logTag, string errorMessage)
    {
        try
        {
            context.StartService(intent);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn(logTag, $"{errorMessage} Details: {exception}");
            return false;
        }
    }
}