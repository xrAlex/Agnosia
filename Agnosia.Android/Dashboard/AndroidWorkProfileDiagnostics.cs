using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Dashboard;

internal static class AndroidWorkProfileDiagnosticsReader
{
    private const string LogTag = "AgnosiaProfileDiagnostics";

    public static WorkProfileDiagnostics Read(Context context)
    {
        var storage = LocalStorageManager.Instance;
        var userManager = AndroidSystemApi.GetUserManager(context);
        var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(context);
        var notes = new List<string>();

        var currentUser = GetCurrentUser(notes);
        var currentUserSerial = GetUserSerial(userManager, currentUser, notes);
        var userProfiles = ReadUserProfiles(userManager, notes);
        var targetProfiles = ReadTargetProfiles(crossProfileApps, notes);
        var storedManagedProfileSerial = storage.GetLong(StorageKeys.ManagedProfileUserSerial, -1);
        var managedProfile = SelectManagedProfile(
            userManager,
            userProfiles,
            targetProfiles,
            currentUser,
            storedManagedProfileSerial,
            notes);

        var managedProfileSerial = GetManagedProfileSerial(
            userManager,
            managedProfile,
            storedManagedProfileSerial,
            notes);
        var managedProfileExists = managedProfile is not null
                                   || userProfiles.Any(profile => !IsSameUser(userManager, currentUser, profile, notes))
                                   || targetProfiles.Count > 0;
        var availableToCrossProfileApps = managedProfile is not null
            ? targetProfiles.Any(target => IsSameUser(userManager, managedProfile, target, notes))
            : targetProfiles.Count > 0;
        var commandTargetResolvable = TryHasWorkProfileTarget(context, notes);
        var quietModeEnabled = managedProfile is null
            ? null
            : TryReadQuietMode(userManager, managedProfile, notes);
        var userRunning = managedProfile is null
            ? null
            : TryReadUserRunning(userManager, managedProfile, notes);
        var userUnlocked = managedProfile is null
            ? null
            : TryReadUserUnlocked(userManager, managedProfile, notes);

        return new WorkProfileDiagnostics(
            managedProfileExists,
            availableToCrossProfileApps,
            quietModeEnabled,
            userRunning,
            userUnlocked,
            commandTargetResolvable,
            currentUserSerial,
            managedProfileSerial,
            ToSafeHandleString(managedProfile),
            userProfiles.Count,
            targetProfiles.Count,
            string.Join(",", notes.Distinct(StringComparer.Ordinal)));
    }

    private static UserHandle? GetCurrentUser(List<string> notes)
    {
        try
        {
            return Process.MyUserHandle();
        }
        catch (Exception exception)
        {
            notes.Add($"currentUser=error:{exception.GetType().Name}");
            Log.Warn(LogTag, $"Could not read current user handle: {exception.GetType().Name}");
            return null;
        }
    }

    private static IReadOnlyList<UserHandle> ReadUserProfiles(UserManager? userManager, List<string> notes)
    {
        if (userManager is null)
        {
            notes.Add("userProfiles=unavailable:userManagerMissing");
            return [];
        }

        try
        {
            return userManager.UserProfiles?.ToArray() ?? [];
        }
        catch (Exception exception)
        {
            notes.Add($"userProfiles=error:{exception.GetType().Name}");
            return [];
        }
    }

    private static IReadOnlyList<UserHandle> ReadTargetProfiles(
        global::Android.Content.PM.CrossProfileApps? crossProfileApps,
        List<string> notes)
    {
        if (crossProfileApps is null)
        {
            notes.Add("targetProfiles=unavailable:crossProfileAppsMissing");
            return [];
        }

        try
        {
            return crossProfileApps.TargetUserProfiles?.ToArray() ?? [];
        }
        catch (Exception exception)
        {
            notes.Add($"targetProfiles=error:{exception.GetType().Name}");
            return [];
        }
    }

    private static UserHandle? SelectManagedProfile(
        UserManager? userManager,
        IReadOnlyList<UserHandle> userProfiles,
        IReadOnlyList<UserHandle> targetProfiles,
        UserHandle? currentUser,
        long storedManagedProfileSerial,
        List<string> notes)
    {
        if (storedManagedProfileSerial >= 0
            && TryGetUserForSerial(userManager, storedManagedProfileSerial, notes) is { } storedUser
            && !IsSameUser(userManager, currentUser, storedUser, notes))
            return storedUser;

        if (targetProfiles.Count > 0) return targetProfiles[0];

        return userProfiles.FirstOrDefault(profile => !IsSameUser(userManager, currentUser, profile, notes));
    }

    private static UserHandle? TryGetUserForSerial(
        UserManager? userManager,
        long userSerial,
        List<string> notes)
    {
        if (userManager is null) return null;

        try
        {
            return userManager.GetUserForSerialNumber(userSerial);
        }
        catch (Exception exception)
        {
            notes.Add($"storedUserLookup=error:{exception.GetType().Name}");
            return null;
        }
    }

    private static long? GetManagedProfileSerial(
        UserManager? userManager,
        UserHandle? managedProfile,
        long storedManagedProfileSerial,
        List<string> notes)
    {
        if (managedProfile is not null) return GetUserSerial(userManager, managedProfile, notes);

        return storedManagedProfileSerial >= 0 ? storedManagedProfileSerial : null;
    }

    private static long? GetUserSerial(
        UserManager? userManager,
        UserHandle? userHandle,
        List<string> notes)
    {
        if (userManager is null || userHandle is null) return null;

        try
        {
            var serial = userManager.GetSerialNumberForUser(userHandle);
            return serial >= 0 ? serial : null;
        }
        catch (Exception exception)
        {
            notes.Add($"userSerial=error:{exception.GetType().Name}");
            return null;
        }
    }

    private static bool IsSameUser(
        UserManager? userManager,
        UserHandle? left,
        UserHandle? right,
        List<string> notes)
    {
        if (left is null || right is null) return false;

        var leftSerial = GetUserSerial(userManager, left, notes);
        var rightSerial = GetUserSerial(userManager, right, notes);
        if (leftSerial is not null && rightSerial is not null) return leftSerial.Value == rightSerial.Value;

        return left.Equals(right);
    }

    private static bool TryHasWorkProfileTarget(Context context, List<string> notes)
    {
        try
        {
            return AgnosiaUtilities.HasWorkProfileTarget(context);
        }
        catch (Exception exception)
        {
            notes.Add($"commandTarget=error:{exception.GetType().Name}");
            return false;
        }
    }

    private static bool? TryReadQuietMode(
        UserManager? userManager,
        UserHandle managedProfile,
        List<string> notes)
    {
        return TryReadManagedProfileFlag(
            userManager,
            managedProfile,
            notes,
            "quietMode",
            static (manager, profile) => manager.IsQuietModeEnabled(profile));
    }

    private static bool? TryReadUserRunning(
        UserManager? userManager,
        UserHandle managedProfile,
        List<string> notes)
    {
        return TryReadManagedProfileFlag(
            userManager,
            managedProfile,
            notes,
            "userRunning",
            static (manager, profile) => manager.IsUserRunning(profile));
    }

    private static bool? TryReadManagedProfileFlag(
        UserManager? userManager,
        UserHandle managedProfile,
        List<string> notes,
        string noteName,
        Func<UserManager, UserHandle, bool> readFlag)
    {
        if (userManager is null) return null;

        try
        {
            return readFlag(userManager, managedProfile);
        }
        catch (Exception exception)
        {
            notes.Add($"{noteName}=error:{exception.GetType().Name}");
            return null;
        }
    }

    private static bool? TryReadUserUnlocked(
        UserManager? userManager,
        UserHandle managedProfile,
        List<string> notes)
    {
        _ = userManager;
        _ = managedProfile;
        notes.Add("userUnlocked=unavailable:bindingNoProfileOverload");
        return null;
    }

    private static string? ToSafeHandleString(UserHandle? userHandle)
    {
        return userHandle?.ToString();
    }
}

internal sealed record WorkProfileDiagnostics(
    bool ManagedProfileExists,
    bool AvailableToCrossProfileApps,
    bool? QuietModeEnabled,
    bool? UserRunning,
    bool? UserUnlocked,
    bool CommandTargetResolvable,
    long? CurrentUserSerial,
    long? ManagedProfileUserSerial,
    string? ManagedProfileHandle,
    int UserProfileCount,
    int TargetProfileCount,
    string Notes)
{
    public string ToLogString()
    {
        return $"managedProfileExists={ManagedProfileExists}; " +
               $"crossProfileAvailable={AvailableToCrossProfileApps}; " +
               $"quietMode={FormatNullable(QuietModeEnabled)}; " +
               $"userRunning={FormatNullable(UserRunning)}; " +
               $"userUnlocked={FormatNullable(UserUnlocked)}; " +
               $"commandTarget={CommandTargetResolvable}; " +
               $"currentUserSerial={CurrentUserSerial?.ToString() ?? "unknown"}; " +
               $"managedUserSerial={ManagedProfileUserSerial?.ToString() ?? "unknown"}; " +
               $"managedHandle={ManagedProfileHandle ?? "unknown"}; " +
               $"userProfiles={UserProfileCount}; targetProfiles={TargetProfileCount}; " +
               $"notes={Notes}";
    }

    private static string FormatNullable(bool? value)
    {
        return value?.ToString() ?? "unknown";
    }
}
