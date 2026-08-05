using Agnosia.Android.Api.Commands;
using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandIntentMapperTests
{
    private static readonly HashSet<AndroidCommandKind> BinderOnlyKinds =
    [
        AndroidCommandKind.RecoverAuthentication,
        AndroidCommandKind.QueryPackageState,
        AndroidCommandKind.WorkAppFrozen
    ];

    public static TheoryData<string, string> RequiredActionMappings => new()
    {
        { nameof(AndroidCommandKind.ProfilePing), AgnosiaActions.ProfilePing },
        { nameof(AndroidCommandKind.QueryApps), AgnosiaActions.QueryApps },
        { nameof(AndroidCommandKind.QueryAppIcon), AgnosiaActions.QueryAppIcon },
        { nameof(AndroidCommandKind.QueryAppIcons), AgnosiaActions.QueryAppIcons },
        { nameof(AndroidCommandKind.QueryLogs), AgnosiaActions.QueryLogs },
        { nameof(AndroidCommandKind.QueryCrossProfilePackages), AgnosiaActions.QueryCrossProfilePackages },
        { nameof(AndroidCommandKind.QueryPermissions), AgnosiaActions.QueryPermissions },
        { nameof(AndroidCommandKind.QueryUsageStatsAccess), AgnosiaActions.QueryUsageStatsAccess },
        { nameof(AndroidCommandKind.QueryPackageInstallAccess), AgnosiaActions.QueryPackageInstallAccess },
        { nameof(AndroidCommandKind.QueryAllFilesAccess), AgnosiaActions.QueryAllFilesAccess }
    };

    [Theory]
    [MemberData(nameof(RequiredActionMappings))]
    public void ToAction_MapsRequiredCommandKinds(string kindName, string expectedAction)
    {
        var kind = Enum.Parse<AndroidCommandKind>(kindName);

        var action = AndroidCommandIntentMapper.ToAction(kind);

        Assert.Equal(expectedAction, action);
    }

    [Fact]
    public void PayloadJsonExtraKey_UsesStableContractKey()
    {
        Assert.Equal("agnosia.command.payload_json", AndroidCommandIntentMapper.PayloadJsonExtraKey);
    }

    [Fact]
    public void ToAction_MapsEveryActivityCommandKindToTargetProfileActivityAction()
    {
        var targetProfileActions = AgnosiaActions.TargetProfileActivityActions.ToHashSet(StringComparer.Ordinal);

        foreach (var kind in Enum.GetValues<AndroidCommandKind>()
                     .Where(kind => !BinderOnlyKinds.Contains(kind)))
        {
            var action = AndroidCommandIntentMapper.ToAction(kind);

            Assert.Contains(action, targetProfileActions);
        }
    }

    [Theory]
    [InlineData(nameof(AndroidCommandKind.RecoverAuthentication))]
    [InlineData(nameof(AndroidCommandKind.QueryPackageState))]
    [InlineData(nameof(AndroidCommandKind.WorkAppFrozen))]
    public void ToAction_RejectsBinderOnlyCommands(string kindName)
    {
        var kind = Enum.Parse<AndroidCommandKind>(kindName);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AndroidCommandIntentMapper.ToAction(kind));
    }

    [Fact]
    public void TryFromAction_RoundTripsEveryActivityCommandKind()
    {
        foreach (var kind in Enum.GetValues<AndroidCommandKind>()
                     .Where(kind => !BinderOnlyKinds.Contains(kind)))
        {
            var action = AndroidCommandIntentMapper.ToAction(kind);

            Assert.True(AndroidCommandIntentMapper.TryFromAction(action, out var actual));
            Assert.Equal(kind, actual);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("agnosia.action.UNKNOWN")]
    [InlineData(AgnosiaActions.WorkAppFrozen)]
    public void TryFromAction_RejectsUnknownActions(string? action)
    {
        Assert.False(AndroidCommandIntentMapper.TryFromAction(action, out _));
    }
}
