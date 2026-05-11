using Android.Content;
using AndroidX.Core.App;

namespace Agnosia.Android.Api;

public static class AndroidNotificationApi
{
    public static Notification BuildNotification(
        Context context,
        string channelId,
        string channelName,
        string channelDescription,
        string title,
        string text,
        int smallIconResourceId,
        bool minimized = false)
    {
        EnsureNotificationChannel(
            context,
            channelId,
            channelName,
            channelDescription,
            minimized ? NotificationImportance.Min : NotificationImportance.Low);

        var builder = new NotificationCompat.Builder(context, channelId);
        builder.SetContentTitle(title);
        builder.SetContentText(text);
        builder.SetSmallIcon(smallIconResourceId);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetShowWhen(false);
        builder.SetCategory(NotificationCompat.CategoryService);
        builder.SetPriority((int)(minimized ? NotificationPriority.Min : NotificationPriority.Low));

        if (minimized)
        {
            builder.SetSilent(true);
            builder.SetLocalOnly(true);
            builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceDeferred);
        }

        return builder.Build()
            ?? throw new InvalidOperationException("Android could not build a notification.");
    }

    private static void EnsureNotificationChannel(
        Context context,
        string channelId,
        string channelName,
        string channelDescription,
        NotificationImportance importance = NotificationImportance.Low)
    {
        if (AndroidSystemApi.GetNotificationManager(context) is not { } manager
            || manager.GetNotificationChannel(channelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(channelId, channelName, importance)
        {
            Description = channelDescription
        };
        manager.CreateNotificationChannel(channel);
    }
}
