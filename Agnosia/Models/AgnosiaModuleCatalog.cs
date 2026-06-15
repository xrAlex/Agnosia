namespace Agnosia.Models;

public static class AgnosiaModuleCatalog
{
    public static AgnosiaModuleMetadata FileShuttle { get; } = new(
        AgnosiaModuleKind.FileShuttle,
        "File Shuttle",
        "Передача файлов между личным и рабочим профилем.",
        "File Shuttle показывает хранилище второго профиля в системном приложении Files, благодаря этому можно безопасно передавать файлы в рабочий профиль из основногои назад.");

    public static AgnosiaModuleMetadata VpnGuard { get; } = new(
        AgnosiaModuleKind.VpnGuard,
        "VPN Guard",
        "Отключение VPN перед запуском рабочего приложения и включение после заморозки.",
        "VPN Guard предотвращает возможность видеть ваш VPN приложениям из рабочего профиля, отключая его при запуске такого приложения и включая после закрытия.");

    public static AgnosiaModuleMetadata Lockdown { get; } = new(
        AgnosiaModuleKind.Lockdown,
        "Lockdown",
        "Блокировка интернета выбранным приложениям рабочего профиля.",
        "Lockdown позволяет заблокировать доступ к интернету приложениям в рабочем профиле, используя always ON VPN (у вас будет гореть значок VPN пока акутивен модуль), не влияет на основной профиль.");

    public static AgnosiaModuleMetadata RiskEngine { get; } = new(
        AgnosiaModuleKind.RiskEngine,
        "Risk Engine",
        "Анализ риска приложений по разрешениям, специальным доступам и runtime-состояниям.",
        "Risk Engine оценивает приложения по комбинациям разрешений, специальных доступов и runtime-состояний, чтобы подсветить опасные конфигурации в каталоге.");

    public static AgnosiaModuleMetadata Get(AgnosiaModuleKind kind)
    {
        return kind switch
        {
            AgnosiaModuleKind.FileShuttle => FileShuttle,
            AgnosiaModuleKind.Lockdown => Lockdown,
            AgnosiaModuleKind.VpnGuard => VpnGuard,
            AgnosiaModuleKind.RiskEngine => RiskEngine,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Agnosia module kind.")
        };
    }
}
