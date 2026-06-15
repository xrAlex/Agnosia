namespace Agnosia.Models;

public static class AgnosiaModuleCatalog
{
    public static AgnosiaModuleMetadata FileShuttle { get; } = new(
        AgnosiaModuleKind.FileShuttle,
        "File Shuttle",
        "Передача файлов между личным и рабочим профилем через Files / DocumentsUI.",
        "File Shuttle показывает хранилище второго профиля в системном Files / DocumentsUI и передает выбранные файлы через content:// URI Android.");

    public static AgnosiaModuleMetadata VpnGuard { get; } = new(
        AgnosiaModuleKind.VpnGuard,
        "VPN Guard",
        "Временное отключение VPN перед запуском рабочего приложения и возврат после заморозки.",
        "VPN Guard управляет VPN-сценарием вокруг скрытых рабочих приложений: перед запуском временно освобождает VPN-слот, а после заморозки отправляет команду выбранному VPN-клиенту.");

    public static AgnosiaModuleMetadata Lockdown { get; } = new(
        AgnosiaModuleKind.Lockdown,
        "Lockdown",
        "Блокировка интернета выбранным приложениям рабочего профиля.",
        "Lockdown использует always-on VPN lockdown в рабочем профиле, чтобы выбранные приложения оставались без доступа к сети.");

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
