namespace Agnosia.Models;

public sealed record AgnosiaModuleSnapshot(
    AgnosiaModuleKind Kind,
    string Title,
    string ShortDescription,
    string FullDescription,
    bool IsEnabled,
    AgnosiaModuleState State,
    IReadOnlyList<AgnosiaModuleRequirement> Requirements,
    string StatusText,
    bool CanSetEnabled)
{
    public static AgnosiaModuleSnapshot FileShuttleUnavailable { get; } = new(
        AgnosiaModuleKind.FileShuttle,
        "File Shuttle",
        "Передача файлов между личным и рабочим профилем через Files / DocumentsUI.",
        "File Shuttle показывает хранилище второго профиля в системном Files / DocumentsUI и передает выбранные файлы через content:// URI Android.",
        false,
        AgnosiaModuleState.Unavailable,
        [],
        "Недоступен",
        false);

    public static AgnosiaModuleSnapshot VpnGuardUnavailable { get; } = new(
        AgnosiaModuleKind.VpnGuard,
        "VPN Guard",
        "Временное отключение VPN перед запуском рабочего приложения и возврат после заморозки.",
        "VPN Guard управляет VPN-сценарием вокруг скрытых рабочих приложений: перед запуском временно освобождает VPN-слот, а после заморозки отправляет команду выбранному VPN-клиенту.",
        false,
        AgnosiaModuleState.Unavailable,
        [],
        "Недоступен",
        false);
}
