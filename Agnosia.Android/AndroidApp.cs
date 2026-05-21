using Agnosia.Android.Api.Platform;
using Agnosia.Android.Infrastructure;
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

        var suppressPrimaryUiStartup = AndroidStartup.TryIsProfileOwner(
            this,
            nameof(AndroidApp),
            "Profile-owner application startup check failed");

        if (suppressPrimaryUiStartup)
        {
            AndroidStartup.SuppressPrimaryUiStartup();
            return;
        }

        AndroidStartup.ConfigurePrimaryProfileServices();
    }
}
