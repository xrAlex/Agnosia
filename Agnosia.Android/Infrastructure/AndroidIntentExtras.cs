using Agnosia.Android.Api.Commands;
using Android.Content;
using Android.OS;
using Java.Lang;

namespace Agnosia.Android.Infrastructure;

internal static class AndroidIntentExtras
{
    public static PendingIntent? ReadParentFrozenCallback(Intent? intent)
    {
        if (intent is null) return null;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return intent.GetParcelableExtra(
                AndroidCommandContract.ExtraParentFrozenCallback,
                Class.FromType(typeof(PendingIntent))) as PendingIntent;

#pragma warning disable CA1422
        return intent.GetParcelableExtra(AndroidCommandContract.ExtraParentFrozenCallback) as PendingIntent;
#pragma warning restore CA1422
    }

    public static Messenger? ReadFileShuttleCallbackMessenger(Intent? intent)
    {
        if (intent is null) return null;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return intent.GetParcelableExtra(
                AndroidCommandContract.ExtraFileShuttleCallbackMessenger,
                Class.FromType(typeof(Messenger))) as Messenger;

#pragma warning disable CA1422
        return intent.GetParcelableExtra(AndroidCommandContract.ExtraFileShuttleCallbackMessenger) as Messenger;
#pragma warning restore CA1422
    }

    public static Messenger? ReadMessenger(Bundle? bundle, string key)
    {
        if (bundle is null) return null;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return bundle.GetParcelable(key, Class.FromType(typeof(Messenger))) as Messenger;

#pragma warning disable CA1422
        return bundle.GetParcelable(key) as Messenger;
#pragma warning restore CA1422
    }
}
