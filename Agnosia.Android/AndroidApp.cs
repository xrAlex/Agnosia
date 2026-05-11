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
        ServiceRegistry.PlatformBridge = AndroidPlatformBridge.Instance;
        ServiceRegistry.InitialTheme = AndroidSettingsStore.LoadAppTheme(LocalStorageManager.Instance);
    }
}
