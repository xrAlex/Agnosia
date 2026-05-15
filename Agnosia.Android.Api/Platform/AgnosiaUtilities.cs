using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Internal;
using Agnosia.Android.Api.Storage;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.Lang;

namespace Agnosia.Android.Api.Platform;

public static class AgnosiaUtilities
{
    private static readonly string[] ParentToManagedActions = AgnosiaActions.ParentToManagedCommandActions;
    private static readonly string[] ManagedToParentActions = AgnosiaActions.ManagedToParentCommandActions;

    public static ComponentName GetAdminComponent(Context context, Type adminReceiverType)
    {
        return new ComponentName(context, Class.FromType(adminReceiverType));
    }

    public static bool IsProfileOwner(Context context)
    {
        return AndroidSystemApi.GetDevicePolicyManager(context) is { } manager
               && manager.IsProfileOwnerApp(context.PackageName);
    }

    public static void TransferIntentToProfile(Context context, Intent intent)
    {
        TransferIntentToProfileUnsigned(context, intent);
        AuthenticationUtility.SignIntent(intent);
    }

    private static void TransferIntentToProfileUnsigned(Context context, Intent intent)
    {
        if (!TryTransferIntentToProfileUnsigned(context, intent))
            throw new InvalidOperationException("Could not resolve the target-profile activity.");
    }

    private static bool TryTransferIntentToProfileUnsigned(Context context, Intent intent)
    {
        var target = ResolveProfileTargetActivity(context, intent);
        if (target?.ActivityInfo is null) return false;

        var packageName = target.ActivityInfo.PackageName;
        var activityName = target.ActivityInfo.Name;
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(activityName))
            throw new InvalidOperationException("Android returned incomplete target-profile activity data.");

        intent.SetComponent(new ComponentName(packageName, activityName));
        return true;
    }

    public static bool HasWorkProfileTarget(Context context)
    {
        var intent = new Intent(AgnosiaActions.ProfilePing);
        return TryTransferIntentToProfileUnsigned(context, intent);
    }

    public static bool HasAssociatedProfile(Context context)
    {
        if (AndroidSystemApi.GetCrossProfileApps(context) is not { } crossProfileApps) return false;

        var targetProfiles = crossProfileApps.TargetUserProfiles;
        if (targetProfiles.Count == 0) return false;

        if (!AndroidApiLevel.IsAtLeastVanillaIceCream()) return true;

        foreach (var userHandle in targetProfiles)
            if (crossProfileApps.IsManagedProfile(userHandle))
                return true;

        return false;
    }

    public static void MarkWorkProfileReady()
    {
        var storage = LocalStorageManager.Instance;
        storage.SetBoolean(StorageKeys.IsSettingUp, false);
        storage.SetBoolean(StorageKeys.HasSetup, true);
        storage.Remove(StorageKeys.SetupStartedAtUtc);
    }

    public static void MarkWorkProfileSetupStarted()
    {
        var storage = LocalStorageManager.Instance;
        storage.SetBoolean(StorageKeys.IsSettingUp, true);
        storage.SetBoolean(StorageKeys.HasSetup, false);
        storage.SetLong(StorageKeys.SetupStartedAtUtc, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        storage.Remove(StorageKeys.ManagedProfileProvisionedAtUtc);
        storage.Remove(StorageKeys.ManagedProfileUserHandle);
        storage.Remove(StorageKeys.ManagedProfileUserSerial);
    }

    public static void MarkManagedProfileProvisioned(Context context, Intent? intent)
    {
        var storage = LocalStorageManager.Instance;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        storage.SetBoolean(StorageKeys.IsSettingUp, true);
        storage.SetBoolean(StorageKeys.HasSetup, false);
        if (storage.GetLong(StorageKeys.SetupStartedAtUtc) <= 0) storage.SetLong(StorageKeys.SetupStartedAtUtc, now);

        storage.SetLong(StorageKeys.ManagedProfileProvisionedAtUtc, now);

        var managedProfileUser = AndroidProvisioningApi.GetManagedProfileUserHandle(intent);
        if (managedProfileUser is null) return;

        storage.SetString(StorageKeys.ManagedProfileUserHandle, managedProfileUser.ToString());
        var userSerial = AndroidSystemApi.GetUserManager(context)?.GetSerialNumberForUser(managedProfileUser) ?? -1;
        if (userSerial >= 0)
            storage.SetLong(StorageKeys.ManagedProfileUserSerial, userSerial);
        else
            storage.Remove(StorageKeys.ManagedProfileUserSerial);
    }

    public static void ClearWorkProfileConfiguredState()
    {
        var storage = LocalStorageManager.Instance;
        storage.SetBoolean(StorageKeys.IsSettingUp, false);
        storage.SetBoolean(StorageKeys.HasSetup, false);
        storage.SetBoolean(StorageKeys.OnboardingCompleted, false);
        storage.Remove(StorageKeys.SetupStartedAtUtc);
        storage.Remove(StorageKeys.ManagedProfileProvisionedAtUtc);
        storage.Remove(StorageKeys.ManagedProfileUserHandle);
        storage.Remove(StorageKeys.ManagedProfileUserSerial);
    }

    private static void DisableMainLauncherActivity(Context context, Type mainActivityType)
    {
        if (context.PackageManager is not { } packageManager) return;

        var component = new ComponentName(context, Class.FromType(mainActivityType));
        if (packageManager.GetComponentEnabledSetting(component) == ComponentEnabledState.Disabled) return;

        packageManager.SetComponentEnabledSetting(
            component,
            ComponentEnabledState.Disabled,
            ComponentEnableOption.DontKillApp);
    }

    public static void EnforceWorkProfilePolicies(Context context, Type adminReceiverType, Type mainActivityType,
        bool enableProfile = false)
    {
        DisableMainLauncherActivity(context, mainActivityType);
        EnsureCrossProfileCommandActionsRegistered();

        if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } manager) return;

        var admin = GetAdminComponent(context, adminReceiverType);
        if (enableProfile) manager.SetProfileEnabled(admin);

        manager.ClearCrossProfileIntentFilters(admin);

        foreach (var action in ParentToManagedActions) AddParentToManagedCrossProfileIntent(manager, admin, action);

        foreach (var action in ManagedToParentActions) AddManagedToParentCrossProfileIntent(manager, admin, action);

        AndroidPolicyApi.ApplyCrossProfileContactsPolicy(manager, admin,
            SettingsManager.Instance.GetBlockContactsSearchingEnabled());
        AndroidPolicyApi.TryEnsureRequiredCrossProfilePackages(manager, admin, nameof(AgnosiaUtilities));
    }

    public static void EnforceUserRestrictions(Context context, Type adminReceiverType)
    {
        if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } manager) return;

        var admin = GetAdminComponent(context, adminReceiverType);
        manager.ClearUserRestriction(admin, UserManager.DisallowInstallApps);
        manager.ClearUserRestriction(admin, UserManager.DisallowInstallUnknownSources);
        manager.ClearUserRestriction(admin, UserManager.DisallowUninstallApps);
        AndroidPolicyApi.AddParentProfileAppLinking(manager, admin);
    }

    private static void AddCrossProfileIntent(DevicePolicyManager manager, ComponentName admin, string action,
        DevicePolicyManagerFlags flag)
    {
        var filter = new IntentFilter(action);
        filter.AddCategory(Intent.CategoryDefault);
        manager.AddCrossProfileIntentFilter(admin, filter, flag);
    }

    private static void AddParentToManagedCrossProfileIntent(DevicePolicyManager manager, ComponentName admin,
        string action)
    {
        AddCrossProfileIntent(manager, admin, action, DevicePolicyManagerFlags.ManagedCanAccessParent);
    }

    private static void AddManagedToParentCrossProfileIntent(DevicePolicyManager manager, ComponentName admin,
        string action)
    {
        AddCrossProfileIntent(manager, admin, action, DevicePolicyManagerFlags.ParentCanAccessManaged);
    }

    private static void EnsureCrossProfileCommandActionsRegistered()
    {
        var duplicateActions = ParentToManagedActions
            .Concat(ManagedToParentActions)
            .GroupBy(action => action, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateActions.Length > 0)
            throw new InvalidOperationException(
                "Duplicate cross-profile command action registration: " + string.Join(", ", duplicateActions));

        EnsureActionsRegistered(
            AgnosiaActions.ParentToManagedCommandActions,
            ParentToManagedActions,
            "parent-to-managed");
        EnsureActionsRegistered(
            AgnosiaActions.ManagedToParentCommandActions,
            ManagedToParentActions,
            "managed-to-parent");
        EnsureTargetProfileActivityActionsRegistered();
    }

    private static void EnsureActionsRegistered(
        IEnumerable<string> declaredActions,
        IEnumerable<string> registeredActions,
        string direction)
    {
        var registered = registeredActions.ToHashSet(StringComparer.Ordinal);
        var missingActions = declaredActions
            .Where(action => !registered.Contains(action))
            .ToArray();
        if (missingActions.Length == 0) return;

        throw new InvalidOperationException(
            $"Missing {direction} cross-profile command action registration: {string.Join(", ", missingActions)}");
    }

    private static void EnsureTargetProfileActivityActionsRegistered()
    {
        var crossProfileActions = ParentToManagedActions
            .Concat(ManagedToParentActions)
            .ToHashSet(StringComparer.Ordinal);
        var localOnlyActions = AgnosiaActions.LocalOnlyTargetProfileActivityActions.ToHashSet(StringComparer.Ordinal);
        var missingActions = AgnosiaActions.TargetProfileActivityActions
            .Where(action => !localOnlyActions.Contains(action))
            .Where(action => !crossProfileActions.Contains(action))
            .ToArray();
        if (missingActions.Length == 0) return;

        throw new InvalidOperationException(
            "Target-profile activity command actions are missing cross-profile filters: " +
            string.Join(", ", missingActions));
    }

    private static ResolveInfo? ResolveProfileTargetActivity(Context context, Intent intent)
    {
        var flags = AndroidSystemApi.GetQueryIntentActivityFlags();
        var activities = context.PackageManager?.QueryIntentActivities(intent, flags);
        if (activities is null) return null;

        ResolveInfo? fallback = null;
        foreach (var activity in activities)
        {
            if (AndroidSystemApi.IsCrossProfileIntentForwarder(activity)) return activity;

            if (fallback is null
                && !string.Equals(activity.ActivityInfo?.PackageName, context.PackageName, StringComparison.Ordinal))
                fallback = activity;
        }

        return fallback;
    }
}