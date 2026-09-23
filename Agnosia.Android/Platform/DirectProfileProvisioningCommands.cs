using System.Globalization;
using System.Text.RegularExpressions;

namespace Agnosia.Android.Platform;

internal sealed record DirectProfileUser(int Id, long Serial, int? ParentId, bool IsManaged, bool IsUsable);

// Shell output is not a public Android API. Unknown formats stop provisioning before further mutations.
internal static class DirectProfileProvisioningCommands
{
    public const string SetupAction = "com.agnosia.app.action.DIRECT_PROFILE_SETUP";
    public const string SetupReceiver = "com.agnosia.app.DirectProfileSetupReceiver";
    public const string ExtraKey = "provisioning_key";
    public const string ExtraUser = "provisioning_user";

    public static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    // Android 12 timeout kills its immediate child. Bypass pm/am/dpm shell scripts so exec keeps
    // the actual Binder client at that PID; newer timeout implementations also kill the process group.
    public static string AsNativeExecutable(string command) => command switch
    {
        "id -u" => "/system/bin/id -u",
        "dumpsys user" => "/system/bin/dumpsys user",
        _ when command.StartsWith("pm ", StringComparison.Ordinal) => "/system/bin/cmd package " + command[3..],
        _ when command.StartsWith("dpm ", StringComparison.Ordinal) => "/system/bin/cmd device_policy " + command[4..],
        _ when command.StartsWith("am ", StringComparison.Ordinal) => "/system/bin/cmd activity " + command[3..],
        _ => throw new ArgumentException("Unsupported root command.", nameof(command))
    };

    public static string CreateUser(int parentId) =>
        $"pm create-user --profileOf {UserId(parentId, true)} --managed 'Agnosia'";

    public static string InstallExisting(int userId, string packageName) =>
        $"pm install-existing --user {UserId(userId)} {Quote(packageName)}";

    public static string PackagePath(int userId, string packageName) =>
        $"pm path --user {UserId(userId)} {Quote(packageName)}";

    public static string SetOwner(int userId, string component) =>
        $"dpm set-profile-owner --user {UserId(userId)} {Quote(component)}";

    public static string StartUser(int userId) => $"am start-user -w {UserId(userId)}";

    public static string Setup(int userId, string packageName, string key)
    {
        if (!AuthenticationKeyMaterial.IsValid(key)) throw new ArgumentException("Invalid provisioning key.", nameof(key));
        return $"am broadcast --user {UserId(userId)} --receiver-foreground --include-stopped-packages " +
               $"-n {Quote(packageName + "/" + SetupReceiver)} -a {Quote(SetupAction)} " +
               $"--ei {ExtraUser} {UserId(userId)} --es {ExtraKey} {Quote(key)}";
    }

    public static int? ParseCreatedUserId(string output)
    {
        var match = Match(output.Trim(), @"\ASuccess: created user id ([0-9]+)\z");
        return match.Success && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var id)
                             && id is > 0 and <= 21474 ? id : null;
    }

    public static IReadOnlyList<DirectProfileUser> ParseUsers(string output)
    {
        var users = new List<DirectProfileUser>();
        if (!Lines(output).Contains("Users:", StringComparer.Ordinal)) throw new FormatException("Missing user list.");
        foreach (var line in Lines(output).Where(line => line.StartsWith("UserInfo{", StringComparison.Ordinal)))
        {
            var match = Match(line, @"\AUserInfo\{([0-9]+):.*:([0-9a-fA-F]+)\} serialNo=([0-9]+)\b(.*)\z");
            if (!match.Success) throw new FormatException("Unknown user record.");
            var id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var flags = int.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var serial = long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var parent = Match(match.Groups[4].Value, @"\bparentId=([0-9]+)\b");
            var managed = (flags & 0x20) != 0;
            if (managed && !parent.Success) throw new FormatException("Missing managed profile parent.");
            if (users.Any(user => user.Id == id)) throw new FormatException("Duplicate user record.");
            users.Add(new DirectProfileUser(id, serial,
                parent.Success ? int.Parse(parent.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                managed, !line.Contains('<') && (flags & 0x40) == 0));
        }
        if (users.Count == 0) throw new FormatException("Empty user list.");
        return users;
    }

    public static IReadOnlyDictionary<int, string> ParseOwners(string output)
    {
        var lines = Lines(output);
        if (lines.SequenceEqual(["no owners"])) return new Dictionary<int, string>();
        var header = Match(lines.FirstOrDefault() ?? "", @"\A([0-9]+) owners?:\z");
        if (!header.Success || !int.TryParse(header.Groups[1].Value, out var count) || count != lines.Length - 1)
            throw new FormatException("Unknown owner list.");
        var owners = new Dictionary<int, string>();
        foreach (var line in lines.Skip(1))
        {
            var match = Match(line, @"\AUser\s+([0-9]+): admin=([A-Za-z0-9_.$]+)/(\.?[A-Za-z0-9_.$]+),.*(?:DeviceOwner|ProfileOwner).*\z");
            if (!match.Success) throw new FormatException("Unknown owner record.");
            var package = match.Groups[2].Value;
            var component = match.Groups[3].Value;
            if (component.StartsWith('.')) component = package + component;
            if (!owners.TryAdd(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), package + "/" + component))
                throw new FormatException("Duplicate owner record.");
        }
        return owners;
    }

    public static bool IsSetupAcknowledged(string output) =>
        Lines(output).LastOrDefault() == "Broadcast completed: result=-1";

    private static string UserId(int id, bool allowSystem = false)
    {
        if (id < (allowSystem ? 0 : 1) || id > 21474) throw new ArgumentOutOfRangeException(nameof(id));
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static string[] Lines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Match Match(string text, string pattern) =>
        Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
