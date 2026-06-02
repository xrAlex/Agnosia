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
}
