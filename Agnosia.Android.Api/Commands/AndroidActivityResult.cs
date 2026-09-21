#if AGNOSIA_ANDROID
using Android.Content;

namespace Agnosia.Android.Api.Commands;

public readonly record struct AndroidActivityResult(Result ResultCode, Intent? Data)
{
    // Managed provenance, never read from Intent extras supplied by another Activity.
    public bool IsLocalFailure { get; init; }
}
#endif
