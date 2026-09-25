using Agnosia.Models;
using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Agnosia.Android.Permissions;

internal static class AndroidAppPermissionReader
{
    public static IReadOnlyList<AppPermissionSnapshot> Read(Context context, string packageName,
        DevicePolicyManager? manager = null, ComponentName? admin = null)
    {
        var pm = context.PackageManager ?? throw new InvalidOperationException("Список разрешений недоступен.");
        var package = pm.GetPackageInfo(packageName,
            PackageInfoFlags.Permissions | AndroidSystemApi.GetInstalledApplicationFlags());
        if (package?.ApplicationInfo is not { } app || (app.Flags & ApplicationInfoFlags.Installed) == 0)
            throw new InvalidOperationException("Приложение больше не установлено в этом профиле.");

        var isOwner = manager is not null && admin is not null && manager.IsProfileOwnerApp(context.PackageName!);
        var names = package.RequestedPermissions ?? [];
        var flags = package.RequestedPermissionsFlags;
        var permissions = new List<AppPermissionSnapshot>(names.Count);
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (string.IsNullOrWhiteSpace(name)) continue;
            var grantKnown = flags is not null && index < flags.Count;
            var packageGranted = grantKnown && (flags![index] & (int)RequestedPermission.Granted) != 0;
            var state = !grantKnown ? AppPermissionState.Unknown
                : packageGranted ? AppPermissionState.Granted : AppPermissionState.NotGranted;
            try
            {
                var info = pm.GetPermissionInfo(name, PackageInfoFlags.MetaData);
                if (info is null) throw new PackageManager.NameNotFoundException(name);
                var runtime = info.Protection == Protection.Dangerous;
                var special = !runtime && (info.ProtectionFlags & Protection.FlagAppop) != 0;
                var kind = runtime ? AppPermissionKind.Runtime : special ? AppPermissionKind.Special : AppPermissionKind.Manifest;
                var canChange = runtime && isOwner;
                string? reason = runtime
                    ? isOwner ? null : "Запрет доступен только для приложений рабочего профиля Agnosia."
                    : special ? "Специальный доступ управляется Android. Запрет через политику разрешений недоступен."
                    : "Android выдаёт это разрешение при установке или по подписи. Отдельно отозвать его нельзя.";

                if (runtime && isOwner)
                {
                    try
                    {
                        state = manager!.GetPermissionGrantState(admin!, packageName, name) switch
                        {
                            PermissionGrantState.Denied => AppPermissionState.PolicyDenied,
                            PermissionGrantState.Granted => AppPermissionState.PolicyGranted,
                            _ => state
                        };
                    }
                    catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
                    {
                        state = AppPermissionState.Unknown;
                        canChange = false;
                        reason = "Android не позволил прочитать политику этого разрешения.";
                    }
                }
                if (special) state = ReadSpecialAccess(context, app, name, state);
                var canRevokeGrant = canChange && packageGranted
                    && (state is AppPermissionState.Granted or AppPermissionState.PolicyGranted)
                    && (int)app.TargetSdkVersion >= (int)BuildVersionCodes.M;
                if (canChange && packageGranted && !canRevokeGrant && (int)app.TargetSdkVersion < (int)BuildVersionCodes.M)
                    reason = "Для старого приложения Android поддерживает постоянный запрет, но не разовый отзыв доступа.";
                var text = AppPermissionDictionary.Find(name);
                permissions.Add(new AppPermissionSnapshot(name, text?.Label ?? AppPermissionDictionary.UnknownLabel,
                    text?.Description, kind, state, canChange, reason, canRevokeGrant));
            }
            catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                              || AndroidRecoverableException.IsMatch(exception))
            {
                permissions.Add(new AppPermissionSnapshot(name, AppPermissionDictionary.UnknownLabel, null, AppPermissionKind.Unknown,
                    AppPermissionState.Unknown, false, "Android не предоставил сведения об этом разрешении."));
            }
        }
        return permissions.DistinctBy(permission => permission.Name)
            .OrderBy(permission => permission.Kind != AppPermissionKind.Runtime)
            .ThenBy(permission => permission.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static AppPermissionState ReadSpecialAccess(Context context, ApplicationInfo app,
        string permission, AppPermissionState defaultState)
    {
        try
        {
            var op = AppOpsManager.PermissionToOp(permission);
            if (op is null || context.GetSystemService(Context.AppOpsService) is not AppOpsManager ops)
                return AppPermissionState.Unknown;
            return ops.CheckOpNoThrow(op, app.Uid, app.PackageName!) switch
            {
                AppOpsManagerMode.Allowed => AppPermissionState.Granted,
                AppOpsManagerMode.Ignored or AppOpsManagerMode.Errored => AppPermissionState.NotGranted,
                AppOpsManagerMode.Default => defaultState,
                _ => AppPermissionState.Unknown
            };
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return AppPermissionState.Unknown;
        }
    }
}
