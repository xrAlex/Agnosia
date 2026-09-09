using Agnosia.Android.Files;
using Xunit;

namespace Agnosia.Unit.Android.Files;

public sealed class AgnosiaFileShuttleListingStoreTests
{
    [Fact]
    public void Paging_UsesOriginalSnapshotWhenDirectoryChanges()
    {
        var store = new AgnosiaFileShuttleListingStore();
        var original = new[] { Document("a"), Document("b") };
        var first = store.ReadPage("folder", null, 0, 1, 4096, () => original);
        var second = store.ReadPage("folder", first.PageToken, 1, 1, 4096,
            () => throw new InvalidOperationException("Directory must only be enumerated once."));
        Assert.Contains("\"b\"", second.Json);
        Assert.False(second.HasMore);
    }

    [Fact]
    public void Paging_RejectsTokenUsedForDifferentDirectory()
    {
        var store = new AgnosiaFileShuttleListingStore();
        var first = store.ReadPage("a", null, 0, 1, 4096, () => [Document("1"), Document("2")]);
        Assert.Throws<InvalidOperationException>(() => store.ReadPage("b", first.PageToken, 1, 1, 4096,
            () => [Document("3")]));
    }

    private static AgnosiaFileShuttleDocumentInfo Document(string name) => new(name, name, "text/plain", 0, 0, 0);
}
