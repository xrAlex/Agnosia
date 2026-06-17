using Android.App;

namespace Agnosia.Android.Gateways;

internal interface IAndroidActivityHostAccessor
{
    void Attach(IAndroidActivityHost activityHost);

    void Detach();

    IAndroidActivityHost GetRequiredHost();

    Activity GetInitializedActivity();
}
