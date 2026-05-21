using Agnosia.Android.Api.Commands;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Commands;

public sealed class AgnosiaActionsTests
{
    // Проверяет, что action-группы не содержат повторяющихся Android actions.
    [Fact]
    public void Command_action_groups_do_not_contain_duplicates()
    {
        StringConstantContract.AssertUniqueValues(AgnosiaActions.ParentToManagedCommandActions);
        StringConstantContract.AssertUniqueValues(AgnosiaActions.ManagedToParentCommandActions);
        StringConstantContract.AssertUniqueValues(AgnosiaActions.TargetProfileActivityActions);
        StringConstantContract.AssertUniqueValues(AgnosiaActions.LocalOnlyTargetProfileActivityActions);
    }

    // Проверяет, что cross-profile command actions доступны как target-profile activities.
    [Fact]
    public void Cross_profile_command_actions_are_registered_as_target_profile_activity_actions()
    {
        var targetProfileActions = AgnosiaActions.TargetProfileActivityActions.ToHashSet(StringComparer.Ordinal);
        var commandActions = AgnosiaActions.ParentToManagedCommandActions
            .Concat(AgnosiaActions.ManagedToParentCommandActions);

        Assert.All(commandActions, action => Assert.Contains(action, targetProfileActions));
    }

    // Проверяет, что local-only actions не попадают в cross-profile command channel.
    [Fact]
    public void Local_only_target_profile_actions_are_not_cross_profile_commands()
    {
        var commandActions = AgnosiaActions.ParentToManagedCommandActions
            .Concat(AgnosiaActions.ManagedToParentCommandActions)
            .ToHashSet(StringComparer.Ordinal);
        var targetProfileActions = AgnosiaActions.TargetProfileActivityActions.ToHashSet(StringComparer.Ordinal);

        Assert.All(AgnosiaActions.LocalOnlyTargetProfileActivityActions, action =>
        {
            Assert.Contains(action, targetProfileActions);
            Assert.DoesNotContain(action, commandActions);
        });
    }

    // Проверяет namespace и уникальность всех объявленных Android actions.
    [Fact]
    public void Declared_action_constants_use_agnosia_action_namespace()
    {
        var actions = StringConstantContract.ValuesOf(typeof(AgnosiaActions));

        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.StartsWith("agnosia.action.", action));
        StringConstantContract.AssertUniqueValues(actions);
    }
}
