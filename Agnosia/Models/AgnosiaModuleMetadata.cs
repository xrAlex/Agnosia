namespace Agnosia.Models;

public sealed record AgnosiaModuleMetadata(
    AgnosiaModuleKind Kind,
    string Title,
    string ShortDescription,
    string FullDescription);
