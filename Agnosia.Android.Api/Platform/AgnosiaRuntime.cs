using Android.Content;

namespace Agnosia.Android.Api;

public static class AgnosiaRuntime
{
    public static void Initialize(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        LocalStorageManager.Initialize(appContext);
        SettingsManager.Initialize(appContext);
    }
}
