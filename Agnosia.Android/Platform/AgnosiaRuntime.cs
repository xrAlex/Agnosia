using Agnosia.Android.Api.Logging;
using Android.Content;
using AndroidUtilLog = Android.Util.Log;

namespace Agnosia.Android.Platform;

public static class AgnosiaRuntime
{
    public static void Initialize(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        LocalStorageManager.Initialize(appContext);
        SettingsManager.Initialize(appContext);
        AgnosiaLog.SetSink((level, tag, message) =>
        {
            try
            {
                AndroidAppLogArchive.Append(appContext, level, tag, message);
            }
            catch (Exception exception)
            {
                AndroidUtilLog.Warn("AgnosiaLogBridge", $"Failed to save the record into the UI journal: {exception.Message}");
            }
        });
    }
}
