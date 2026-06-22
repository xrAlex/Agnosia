using Agnosia.Android.Api.Commands;
using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandIntentMapperTests
{
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
    public void ToAction_MapsEveryCommandKindToTargetProfileActivityAction()
    {
        var targetProfileActions = AgnosiaActions.TargetProfileActivityActions.ToHashSet(StringComparer.Ordinal);

        foreach (var kind in Enum.GetValues<AndroidCommandKind>())
        {
            var action = AndroidCommandIntentMapper.ToAction(kind);

            Assert.Contains(action, targetProfileActions);
        }
    }
}
