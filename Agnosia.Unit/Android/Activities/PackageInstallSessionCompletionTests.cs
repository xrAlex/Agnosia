using Agnosia.Android.Activities;
using Xunit;

namespace Agnosia.Unit.Android.Activities;

public sealed class PackageInstallSessionCompletionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task System_session_result_completes_without_broadcast(bool success)
    {
        var session = new PackageInstallSessionCompletion(42);
        session.ReportFinished(42, success);
        Assert.True(session.Completion.IsCompleted);
        Assert.Equal(success, await session.Completion);
    }

    [Fact]
    public async Task Other_session_and_duplicate_events_cannot_replace_result()
    {
        var session = new PackageInstallSessionCompletion(42);
        session.ReportFinished(41, true);
        Assert.False(session.Completion.IsCompleted);
        session.ReportFinished(42, false);
        session.ReportFinished(42, true);
        Assert.False(await session.Completion);
    }
}
