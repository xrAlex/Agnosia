using Agnosia.Android.Api;
using Agnosia.Infrastructure;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Agnosia.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        InitializePlatformServices();
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    private void InitializePlatformServices()
    {
        AgnosiaRuntime.Initialize(this);

        var suppressPrimaryUiStartup = false;
        try
        {
            suppressPrimaryUiStartup = AgnosiaUtilities.IsProfileOwner(this);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(nameof(AndroidApp), $"Profile-owner application startup check failed: {exception.Message}");
        }

        if (suppressPrimaryUiStartup)
        {
            ServiceRegistry.SuppressPrimaryUiStartup = true;
            return;
        }

        ServiceRegistry.SuppressPrimaryUiStartup = false;
        ServiceRegistry.PlatformBridge = AndroidPlatformBridge.Instance;
        ServiceRegistry.InitialTheme = AndroidSettingsStore.LoadAppTheme(LocalStorageManager.Instance);
    }
}
