using Agnosia.Android.Api.Logging;
using Agnosia.Android.Infrastructure;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using AndroidUtilLog = Android.Util.Log;

namespace Agnosia.Android.Platform;

public static class AgnosiaRuntime
{
    private static readonly Lock InitializationSync = new();
    private static Context? _applicationContext;

    public static void Initialize(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        EnsureServicesConfigured(appContext);
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

    private static void EnsureServicesConfigured(Context appContext)
    {
        lock (InitializationSync)
        {
            if (ReferenceEquals(_applicationContext, appContext)) return;

            _applicationContext = appContext;
            var services = new ServiceCollection()
                .AddSingleton(appContext)
                .AddAgnosiaCore()
                .AddAgnosiaAndroid()
                .BuildServiceProvider();

            ServiceRegistry.ConfigureServices(services);
        }
    }
}
