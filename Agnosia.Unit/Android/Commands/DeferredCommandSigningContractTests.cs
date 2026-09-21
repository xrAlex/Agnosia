using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class DeferredCommandSigningContractTests
{
    [Fact]
    public void Host_prepares_trusted_command_after_dequeue_and_before_start()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root,
            "Agnosia.Android", "MainActivity.ActivityResults.cs"));
        var start = source[source.IndexOf("private void StartActivityForResultRequest", StringComparison.Ordinal)..];
        var preparation = start.IndexOf("request.BeforeStart?.Invoke()", StringComparison.Ordinal);
        Assert.True(preparation > start.IndexOf("PendingResults.ContainsKey", StringComparison.Ordinal));
        Assert.True(preparation < start.IndexOf("StartActivity(request.Intent)", StringComparison.Ordinal));
        Assert.DoesNotContain("AuthenticationUtility.SignIntent", source);
    }

    [Fact]
    public void Failed_inventory_query_is_not_an_empty_inventory()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root,
            "Agnosia.Android", "Dashboard", "AndroidDashboardReader.cs"));
        Assert.DoesNotContain("payload is null ? AppQueryResult.Empty", source);
        Assert.Contains("Не удалось получить список приложений", source);
    }
}
