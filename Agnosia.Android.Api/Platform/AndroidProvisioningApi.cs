using Android.App.Admin;
using Android.Content;
using Android.OS;
using Java.Lang;

namespace Agnosia.Android.Api.Platform;

public static class AndroidProvisioningApi
{
    public static bool CanStartManagedProfileProvisioning(DevicePolicyManager policyManager)
    {
        return policyManager.IsProvisioningAllowed(DevicePolicyManager.ActionProvisionManagedProfile);
    }

    public static void ConfigureManagedProfileProvisioningIntent(
        Intent intent,
        ComponentName adminComponent,
        string authKey)
    {
        intent.PutExtra(DevicePolicyManager.ExtraProvisioningDeviceAdminComponentName, adminComponent);
        intent.PutExtra(DevicePolicyManager.ExtraProvisioningSkipEncryption, true);

        var adminExtras = new PersistableBundle();
        adminExtras.PutString(AuthenticationUtility.ProvisioningAuthKeyExtra, authKey);
        intent.PutExtra(DevicePolicyManager.ExtraProvisioningAdminExtrasBundle, adminExtras);
    }

    public static bool TryStoreProvisioningAuthKey(Intent? intent)
    {
        var adminExtras = GetProvisioningAdminExtras(intent);
        return AuthenticationUtility.TryStoreProvisioningKey(
            adminExtras?.GetString(AuthenticationUtility.ProvisioningAuthKeyExtra));
    }

    public static UserHandle? GetManagedProfileUserHandle(Intent? intent)
    {
        if (intent is null) return null;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return intent.GetParcelableExtra(
                Intent.ExtraUser,
                Class.FromType(typeof(UserHandle))) as UserHandle;

#pragma warning disable CA1422
        return intent.GetParcelableExtra(Intent.ExtraUser) as UserHandle;
#pragma warning restore CA1422
    }

    private static PersistableBundle? GetProvisioningAdminExtras(Intent? intent)
    {
        if (intent is null) return null;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return intent.GetParcelableExtra(
                DevicePolicyManager.ExtraProvisioningAdminExtrasBundle,
                Class.FromType(typeof(PersistableBundle))) as PersistableBundle;

#pragma warning disable CA1422
        return intent.GetParcelableExtra(DevicePolicyManager.ExtraProvisioningAdminExtrasBundle) as PersistableBundle;
#pragma warning restore CA1422
    }
}