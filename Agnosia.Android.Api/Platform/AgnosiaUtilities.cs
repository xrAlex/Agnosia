using Agnosia.Android.Api.Internal;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.Lang;

namespace Agnosia.Android.Api;

public static class AgnosiaUtilities
{
    private static readonly string[] ParentToManagedActions =
    [
        AgnosiaActions.ProfilePing,
        AgnosiaActions.QueryApps,
        AgnosiaActions.QueryLogs,
        AgnosiaActions.QueryUsageStatsAccess,
        AgnosiaActions.RequestUsageStatsAccess,
        AgnosiaActions.QueryPackageInstallAccess,
        AgnosiaActions.RequestPackageInstallAccess,
        AgnosiaActions.InstallPackage,
        AgnosiaActions.UninstallPackage,
        AgnosiaActions.FreezePackage,
        AgnosiaActions.UnfreezePackage,
        AgnosiaActions.UnfreezeAndLaunch,
        AgnosiaActions.PrepareHiddenShortcut,
        AgnosiaActions.LaunchAppProxy,
        AgnosiaActions.SetCrossProfileInteraction,
        AgnosiaActions.SynchronizePreference
    ];

    private static readonly string[] ManagedToParentActions =
    [
        AgnosiaActions.WorkAppFrozen,
        AgnosiaActions.FinalizeProvision
    ];

    public static ComponentName GetAdminComponent(Context context, Type adminReceiverType) =>
        new(context, Class.FromType(adminReceiverType));

    public static bool IsProfileOwner(Context context) =>
        AndroidSystemApi.GetDevicePolicyManager(context) is { } manager
        && manager.IsProfileOwnerApp(context.PackageName);

    public static void TransferIntentToProfile(Context context, Intent intent)
    {
        TransferIntentToProfileUnsigned(context, intent);
        AuthenticationUtility.SignIntent(intent);
    }

    private static void TransferIntentToProfileUnsigned(Context context, Intent intent)
    {
        if (!TryTransferIntentToProfileUnsigned(context, intent))
        {
            throw new InvalidOperationException("Could not resolve the target-profile activity.");
        }
    }

    private static bool TryTransferIntentToProfileUnsigned(Context context, Intent intent)
    {
        var target = ResolveProfileTargetActivity(context, intent);
        if (target?.ActivityInfo is null)
        {
            return false;
        }

        var packageName = target.ActivityInfo.PackageName;
        var activityName = target.ActivityInfo.Name;
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(activityName))
        {
            throw new InvalidOperationException("Android returned incomplete target-profile activity data.");
        }

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
        if (AndroidSystemApi.GetCrossProfileApps(context) is not { } crossProfileApps)
        {
            return false;
        }

        var targetProfiles = crossProfileApps.TargetUserProfiles;
        if (targetProfiles.Count == 0)
        {
            return false;
        }

        if (!AndroidApiLevel.IsAtLeastVanillaIceCream())
        {
            return true;
        }

        foreach (var userHandle in targetProfiles)
        {
            if (crossProfileApps.IsManagedProfile(userHandle))
            {
                return true;
            }
        }

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
    }

    public static void ClearWorkProfileConfiguredState()
    {
        var storage = LocalStorageManager.Instance;
        storage.SetBoolean(StorageKeys.IsSettingUp, false);
        storage.SetBoolean(StorageKeys.HasSetup, false);
        storage.SetBoolean(StorageKeys.OnboardingCompleted, false);
        storage.Remove(StorageKeys.SetupStartedAtUtc);
    }

    public static void EnforceWorkProfilePolicies(Context context, Type adminReceiverType, Type mainActivityType, bool enableProfile = false)
    {
        if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } manager)
        {
            return;
        }

        var admin = GetAdminComponent(context, adminReceiverType);
        if (enableProfile)
        {
            manager.SetProfileEnabled(admin);
        }

        context.PackageManager?.SetComponentEnabledSetting(
            new ComponentName(context, Class.FromType(mainActivityType)),
            ComponentEnabledState.Disabled,
            ComponentEnableOption.DontKillApp);

        manager.ClearCrossProfileIntentFilters(admin);

        foreach (var action in ParentToManagedActions)
        {
            AddParentToManagedCrossProfileIntent(manager, admin, action);
        }

        foreach (var action in ManagedToParentActions)
        {
            AddManagedToParentCrossProfileIntent(manager, admin, action);
        }

        AndroidPolicyApi.ApplyCrossProfileContactsPolicy(manager, admin, SettingsManager.Instance.GetBlockContactsSearchingEnabled());
    }

    public static void EnforceUserRestrictions(Context context, Type adminReceiverType)
    {
        if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } manager)
        {
            return;
        }

        var admin = GetAdminComponent(context, adminReceiverType);
        manager.ClearUserRestriction(admin, UserManager.DisallowInstallApps);
        manager.ClearUserRestriction(admin, UserManager.DisallowInstallUnknownSources);
        manager.ClearUserRestriction(admin, UserManager.DisallowUninstallApps);
        AndroidPolicyApi.AddParentProfileAppLinking(manager, admin);
    }

    private static void AddCrossProfileIntent(DevicePolicyManager manager, ComponentName admin, string action, DevicePolicyManagerFlags flag)
    {
        var filter = new IntentFilter(action);
        manager.AddCrossProfileIntentFilter(admin, filter, flag);
    }

    private static void AddParentToManagedCrossProfileIntent(DevicePolicyManager manager, ComponentName admin, string action) =>
        AddCrossProfileIntent(manager, admin, action, DevicePolicyManagerFlags.ManagedCanAccessParent);

    private static void AddManagedToParentCrossProfileIntent(DevicePolicyManager manager, ComponentName admin, string action) =>
        AddCrossProfileIntent(manager, admin, action, DevicePolicyManagerFlags.ParentCanAccessManaged);

    private static ResolveInfo? ResolveProfileTargetActivity(Context context, Intent intent)
    {
        var flags = AndroidSystemApi.GetQueryIntentActivityFlags();
        var activities = context.PackageManager?.QueryIntentActivities(intent, flags);
        if (activities is null)
        {
            return null;
        }

        ResolveInfo? fallback = null;
        foreach (var activity in activities)
        {
            if (AndroidSystemApi.IsCrossProfileIntentForwarder(activity))
            {
                return activity;
            }

            if (fallback is null
                && !string.Equals(activity.ActivityInfo?.PackageName, context.PackageName, StringComparison.Ordinal))
            {
                fallback = activity;
            }
        }

        return fallback;
    }
}
