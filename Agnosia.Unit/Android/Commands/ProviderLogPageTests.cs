using Agnosia.Android.Commands;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ProviderLogPageTests
{
    [Fact]
    public void PagesRetainSnapshotWhileNewLogsArriveAndRespectWireBudget()
    {
        var store = new ProviderLogPageStore();
        var token = Guid.NewGuid().ToString("N");
        var entries = Enumerable.Range(0, 100).Select(i => new AppLogEntry(
            i.ToString(), DateTimeOffset.UtcNow, ProfileKind.Work, AppLogLevel.Information, "test", i + new string('Ж', 4000))).ToArray();
        var first = store.Read(new ProviderLogPageRequest(token, 0), () => entries, 1000);
        Assert.True(first.HasMore);
        Assert.True(ProviderLogPageStore.Fits(first));
        var next = first.NextOffset;
        var total = first.NextOffset;
        while (first.HasMore)
        {
            first = store.Read(new ProviderLogPageRequest(token, next), () => throw new Exception("Snapshot must be reused."), 1500);
            Assert.True(ProviderLogPageStore.Fits(first));
            Assert.True(first.NextOffset > next);
            total += first.NextOffset - next;
            next = first.NextOffset;
        }
        Assert.Equal(entries.Length, total);
        Assert.Throws<InvalidOperationException>(() => store.Read(new ProviderLogPageRequest(token, 1), () => [], 70000));
    }
}
