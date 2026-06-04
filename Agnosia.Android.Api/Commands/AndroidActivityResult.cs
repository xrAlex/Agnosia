#if AGNOSIA_ANDROID
using Android.App;
using Android.Content;

namespace Agnosia.Android.Api.Commands;

public readonly record struct AndroidActivityResult(Result ResultCode, Intent? Data);
#endif
