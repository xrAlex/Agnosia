namespace Agnosia.Models;

public sealed record AppLogEntry(
    string Id,
    DateTimeOffset Timestamp,
    ProfileKind Profile,
    AppLogLevel Level,
    string Tag,
    string Message);
