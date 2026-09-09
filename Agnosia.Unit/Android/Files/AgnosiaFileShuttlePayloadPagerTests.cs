using System.Text.Json;
using Agnosia.Android.Files;
using Xunit;

namespace Agnosia.Unit.Android.Files;

public sealed class AgnosiaFileShuttlePayloadPagerTests
{
    // Ловит возврат к одному неограниченному Binder payload для большого каталога.
    [Fact]
    public void CreatePage_returns_every_document_in_bounded_advancing_pages()
    {
        var documents = Enumerable.Range(0, 24)
            .Select(index => Document(index, new string('я', 48)))
            .ToArray();
        var actualIds = new List<string>();
        var offset = 0;

        for (var pageIndex = 0; pageIndex < 24; pageIndex++)
        {
            var page = AgnosiaFileShuttlePayloadPager.CreatePage(documents, offset, 5, 1800);
            var pageDocuments = JsonSerializer.Deserialize(
                page.Json,
                AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfoArray) ?? [];

            Assert.InRange(pageDocuments.Length, 1, 5);
            Assert.True(page.JsonUtf8Bytes <= 1800);
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(page.Json), page.JsonUtf8Bytes);
            Assert.Equal(offset + pageDocuments.Length, page.NextOffset);
            actualIds.AddRange(pageDocuments.Select(document => document.DocumentId));

            if (!page.HasMore) break;
            offset = page.NextOffset;
        }

        Assert.Equal(documents.Select(document => document.DocumentId), actualIds);
    }

    // Ловит отправку даже одного сообщения, которое превышает установленный безопасный предел.
    [Fact]
    public void CreatePage_rejects_a_single_document_that_exceeds_the_payload_limit()
    {
        var oversized = Document(0, new string('x', 4096));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AgnosiaFileShuttlePayloadPager.CreatePage([oversized], 0, 10, 128));

        Assert.Contains("payload", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AgnosiaFileShuttleDocumentInfo Document(int index, string suffix)
    {
        return new AgnosiaFileShuttleDocumentInfo(
            $"/storage/emulated/0/folder/{index:00}-{suffix}",
            $"File {index:00} {suffix}",
            "application/octet-stream",
            index,
            1_700_000_000_000 + index,
            2);
    }
}
