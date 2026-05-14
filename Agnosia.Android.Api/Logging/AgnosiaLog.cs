using Agnosia.Models;
using Android.Util;

namespace Agnosia.Android.Api;

public static class AgnosiaLog
{
    private const string InternalTag = "AgnosiaLogBridge";

    public static void Debug(string tag, string message) =>
        Write(AppLogLevel.Debug, tag, message, static (logTag, logMessage) => Log.Debug(logTag, logMessage));

    public static void Info(string tag, string message) =>
        Write(AppLogLevel.Information, tag, message, static (logTag, logMessage) => Log.Info(logTag, logMessage));

    public static void Warn(string tag, string message) =>
        Write(AppLogLevel.Warning, tag, message, static (logTag, logMessage) => Log.Warn(logTag, logMessage));

    public static void Error(string tag, string message) =>
        Write(AppLogLevel.Error, tag, message, static (logTag, logMessage) => Log.Error(logTag, logMessage));

    private static void Write(
        AppLogLevel level,
        string tag,
        string message,
        Action<string, string> androidLogWriter)
    {
        androidLogWriter(tag, message);

        if (level == AppLogLevel.Debug)
        {
            return;
        }

        try
        {
            var context = Application.Context;
            AndroidAppLogArchive.Append(context, level, tag, message);
        }
        catch (Exception exception)
        {
            Log.Warn(InternalTag, $"Failed to save the record into the UI journal: {exception.Message}");
        }
    }
}
