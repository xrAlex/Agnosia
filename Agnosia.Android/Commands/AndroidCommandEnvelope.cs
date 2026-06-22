namespace Agnosia.Android.Commands;

internal sealed record AndroidCommandEnvelope(
    Guid CorrelationId,
    AndroidCommandKind Kind,
    AndroidCommandTargetProfile TargetProfile,
    AndroidCommandInteractivity Interactivity,
    AndroidCommandPriority Priority,
    TimeSpan Timeout,
    string? PayloadJson);
