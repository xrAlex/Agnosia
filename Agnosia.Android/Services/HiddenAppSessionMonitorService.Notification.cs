using _Microsoft.Android.Resource.Designer;
using Android.Content.PM;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    private void StartForegroundServiceNotification(HiddenAppSessionStoreState state)
    {
        var pending = state.PendingHides.FirstOrDefault();
        var parentNotification = state.PendingParentNotifications.FirstOrDefault();
        var session = pending?.Session ?? parentNotification?.Session ?? state.ActiveSession;
        if (session is null) return;

        var title = pending is not null
            ? $"Не удалось скрыть: {session.DisplayName}"
            : parentNotification is not null
                ? $"Изоляция восстановлена: {session.DisplayName}"
                : $"Открыто: {session.DisplayName}";
        var message = pending is not null
            ? "Agnosia продолжает попытки восстановить изоляцию приложения."
            : parentNotification is not null
                ? "Agnosia ожидает подтверждения основного профиля для восстановления VPN."
                : $"Приложение снова скроется через {UserBackgroundHideDelay.TotalSeconds:0} секунд после сворачивания или закрытия.";
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
