namespace Agnosia.Models;

internal static class PermissionCatalog
{
    private static readonly IReadOnlyDictionary<PermissionKind, PermissionDefinition> Definitions =
        new Dictionary<PermissionKind, PermissionDefinition>
        {
            [PermissionKind.WorkProfile] = new(
                "Рабочий профиль",
                "Основной профиль",
                "Нужен для изоляции клонированных приложений, скрытия пакетов и управления политиками рабочего пространства",
                "Подключен",
                "Создать профиль"),
            [PermissionKind.Notifications] = new(
                "Уведомления",
                "Основной профиль",
                "Необходимо для отображения фоновой активности приложения",
                "Получено",
                "Разрешить"),
            [PermissionKind.VpnControl] = new(
                "Временное управление VPN",
                "Основной профиль",
                "Позволяет приложению управлять VPN-соединениями",
                "Получено",
                "Разрешить"),
            [PermissionKind.PackageInstall] = new(
                "Установка APK",
                "Рабочий профиль",
                "Нужна для копирования пользовательских приложений в рабочий профиль через установщик APK",
                "Получено",
                "Открыть настройки"),
            [PermissionKind.PersonalAllFiles] = new(
                "Доступ к файлам",
                "Основной профиль",
                "Нужно для File Shuttle, чтобы Agnosia могла отдавать выбранные файлы личного профиля через DocumentsUI",
                "Получено",
                "Открыть настройки"),
            [PermissionKind.WorkAllFiles] = new(
                "Доступ к файлам",
                "Рабочий профиль",
                "Нужно для File Shuttle, чтобы Agnosia могла отдавать выбранные файлы рабочего профиля через DocumentsUI",
                "Получено",
                "Открыть настройки"),
            [PermissionKind.UsageStats] = new(
                "Доступ к истории использования",
                "Рабочий профиль",
                """
                Позволяет Agnosia понять, когда вы перестали использовать приложение, и заморозить его

                1. Нажмите Разрешить
                2. Пролистайте вниз
                3. Активируйте 'Доступ к истории использования'

                Если на этом шаге Android не выдал разрешение, тогда:

                4. Вернитесь назад
                5. В верхней правой части экрана нажмите на ⋮
                6. Выберите 'Разрешить доступ к настройкам'
                7. Пролистайте вниз
                8. Активируйте 'Доступ к истории использования'
                """,
                "Получено",
                "Открыть настройки"),
            [PermissionKind.Overlay] = new(
                "Поверх окон",
                "Основной профиль",
                """
                Необходимо для показа overlay-окна, которое позволяет запускать VPN после заморозки приложения в рабочем профиле.

                1. Нажмите Разрешить
                2. Пролистайте вниз
                3. Активируйте 'Поверх других приложений'
                
                Если на этом шаге Android не выдал разрешение, тогда:
                
                4. Вернитесь назад
                5. В верхней правой части экрана нажмите на ⋮
                6. Выберите 'Разрешить доступ к настройкам'
                7. Пролистайте вниз
                8. Активируйте 'Поверх других приложений'
                """,
                "Получено",
                "Открыть настройки")
        };

    public static PermissionSnapshot CreateSnapshot(
        PermissionKind kind,
        bool isGranted,
        bool canRequest)
    {
        var definition = GetDefinition(kind);
        return CreateSnapshot(kind, definition, isGranted, canRequest, definition.RequestLabel);
    }

    public static PermissionSnapshot CreateWorkProfileSnapshot(
        bool hasSetup,
        bool workProfileAvailable)
    {
        var definition = GetDefinition(PermissionKind.WorkProfile);
        return CreateSnapshot(
            PermissionKind.WorkProfile,
            definition,
            hasSetup && workProfileAvailable,
            !hasSetup || !workProfileAvailable,
            hasSetup ? "Проверить профиль" : definition.RequestLabel);
    }

    public static AgnosiaModuleRequirement CreateRequirement(
        PermissionSnapshot permission)
    {
        var definition = GetDefinition(permission.Kind);
        return new AgnosiaModuleRequirement(
            definition.Title,
            definition.Description,
            permission.IsGranted,
            permission.Kind,
            definition.RequestLabel);
    }

    private static PermissionDefinition GetDefinition(PermissionKind kind)
    {
        return Definitions.TryGetValue(kind, out var definition)
            ? definition
            : new PermissionDefinition(kind.ToString(), string.Empty, string.Empty, "Получено", "Открыть");
    }

    private static PermissionSnapshot CreateSnapshot(
        PermissionKind kind,
        PermissionDefinition definition,
        bool isGranted,
        bool canRequest,
        string requestLabel)
    {
        return new PermissionSnapshot(
            kind,
            definition.Title,
            definition.ProfileLabel,
            definition.Description,
            isGranted,
            canRequest,
            definition.GrantedLabel,
            requestLabel);
    }

    private sealed record PermissionDefinition(
        string Title,
        string ProfileLabel,
        string Description,
        string GrantedLabel,
        string RequestLabel);
}
