using _Microsoft.Android.Resource.Designer;
using Android.Content.PM;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    private void StartForegroundServiceNotification(HiddenAppSessionStoreState state)
    {
        var pending = state.PendingHides.FirstOrDefault();
        var session = pending?.Session ?? state.ActiveSession;
        if (session is null) return;

        var title = pending is null
            ? $"Открыто: {session.DisplayName}"
            : $"Не удалось скрыть: {session.DisplayName}";
        var message = pending is null
            ? $"Приложение снова скроется через {UserBackgroundHideDelay.TotalSeconds:0} секунд после сворачивания или закрытия."
            : "Agnosia продолжает попытки восстановить изоляцию приложения.";
        var notification = AndroidNotificationApi.BuildNotification(
            this,
            NotificationChannelId,
            NotificationChannelName,
            NotificationChannelDescription,
            title,
            message,
            ResourceConstant.Drawable.icon);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
            return;
        }

        StartForeground(NotificationId, notification);
    }
}
