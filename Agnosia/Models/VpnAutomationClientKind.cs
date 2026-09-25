namespace Agnosia.Models;

public enum VpnAutomationClientKind
{
    FlClash,
    ClashMeta,
    Happ,
    Tunguska,
    Incy,
    Exclave,
    Husi,
    NekoBoxPlus,
    V2RayNg,
    // Legacy saved choices are normalized to the shared client option when loaded.
    V2RayNgFdroid,
    OlcNg,
    // Legacy saved choice is normalized to OlcNg when loaded.
    OlcNgFdroid,
    V2RayTun,
    LxBox,
    Karing
}
