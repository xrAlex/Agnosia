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
    public static AgnosiaModuleSnapshot FileShuttleUnavailable { get; } = Unavailable(
        AgnosiaModuleKind.FileShuttle);

    public static AgnosiaModuleSnapshot VpnGuardUnavailable { get; } = Unavailable(
        AgnosiaModuleKind.VpnGuard);

    public static AgnosiaModuleSnapshot LockdownUnavailable { get; } = Unavailable(
        AgnosiaModuleKind.Lockdown);

    public static AgnosiaModuleSnapshot RiskEngineUnavailable { get; } = Unavailable(
        AgnosiaModuleKind.RiskEngine);

    public static AgnosiaModuleSnapshot Unavailable(AgnosiaModuleKind kind)
    {
        return Create(
            AgnosiaModuleCatalog.Get(kind),
            false,
            AgnosiaModuleState.Unavailable,
            [],
            "Недоступен",
            false);
    }

    public static AgnosiaModuleSnapshot Create(
        AgnosiaModuleMetadata metadata,
        bool isEnabled,
        AgnosiaModuleState state,
        IReadOnlyList<AgnosiaModuleRequirement> requirements,
        string statusText,
        bool canSetEnabled)
    {
        return new AgnosiaModuleSnapshot(
            metadata.Kind,
            metadata.Title,
            metadata.ShortDescription,
            metadata.FullDescription,
            isEnabled,
            state,
            requirements,
            statusText,
            canSetEnabled);
    }
}
