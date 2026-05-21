using Agnosia.Android.Api.Commands;
using Android.App;
using Android.Content;
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
}
