using Agnosia.Android.Activities;
using Xunit;

namespace Agnosia.Unit.Android.Activities;

public sealed class TemporaryPackageVisibilityTransactionTests
{
    [Fact]
    public void NewTransaction_DoesNotRequireRollbackBeforePackageIsUnhidden()
    {
        var transaction = new TemporaryPackageVisibilityTransaction(wasHidden: true);

        Assert.False(transaction.RollbackRequired);
    }

    [Fact]
    public void MarkPackageUnhidden_RequiresRollback_WhenPackageWasHidden()
    {
        var transaction = new TemporaryPackageVisibilityTransaction(wasHidden: true);

        transaction.MarkPackageUnhidden();

        Assert.True(transaction.RollbackRequired);
    }

    [Fact]
    public void Commit_ClearsRollbackRequirement()
    {
        var transaction = new TemporaryPackageVisibilityTransaction(wasHidden: true);
        transaction.MarkPackageUnhidden();

        transaction.Commit();

        Assert.False(transaction.RollbackRequired);
    }

    [Fact]
    public void VisiblePackage_NeverRequiresRollback()
    {
        var transaction = new TemporaryPackageVisibilityTransaction(wasHidden: false);

        transaction.MarkPackageUnhidden();

        Assert.False(transaction.RollbackRequired);
    }
}
