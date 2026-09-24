using Android.Content;
using Uri = Android.Net.Uri;

namespace Agnosia.Android.Activities;

public sealed partial class DummyActivity
{
    private void ActionConnectCommandProvider()
    {
        var sourceUser = Intent?.GetIntExtra(ProviderCommandProtocol.SourceUserExtra, -1) ?? -1;
        var targetUser = Intent?.GetIntExtra(ProviderCommandProtocol.TargetUserExtra, -1) ?? -1;
        var key = AuthenticationUtility.GetExistingKey();
        if (!_isProfileOwner || key is null || targetUser != ProviderProfileIdentity.CurrentUserId
            || !ProviderProfileIdentity.IsSibling(this, sourceUser))
        {
            FinishWithError("Командный доступ разрешён только из связанного личного профиля.", "wrong_profile");
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var access = new ProviderCommandMessage(ProviderCommandProtocol.Version, _commandCorrelationId,
            nameof(AndroidCommandKind.ConnectCommandProvider), targetUser, sourceUser,
            ProviderProfileIdentity.Generation(this, key), now, now + 30_000, true, true,
            $"{ProviderCommandProtocol.Authority}{ProviderCommandProtocol.Path}:write:persistable", null, "", "");
        var result = new Intent();
        var uri = Uri.Parse(ProviderCommandProtocol.AccessUri(targetUser))!;
        result.SetData(uri);
        result.ClipData = ClipData.NewRawUri("Agnosia commands", uri);
        result.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);
        result.PutExtra(ProviderCommandProtocol.BootstrapExtra, ProviderCommandProtocol.Serialize(access));
        FinishWithResult(Result.Ok, result);
    }
}
