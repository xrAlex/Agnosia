using _Microsoft.Android.Resource.Designer;
using Agnosia.Android.Api.Notifications;
using Android.App;
using Android.Content.PM;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    private void StartForegroundServiceNotification(HiddenAppSessionState session)
    {
        var notification = AndroidNotificationApi.BuildNotification(
            this,
            NotificationChannelId,
            NotificationChannelName,
            NotificationChannelDescription,
            $"Открыто: {session.DisplayName}",
            $"Приложение снова скроется через {UserBackgroundHideDelay.TotalSeconds:0} секунд после сворачивания или закрытия.",
            ResourceConstant.Drawable.icon);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
            return;
        }

        StartForeground(NotificationId, notification);
    }
}
