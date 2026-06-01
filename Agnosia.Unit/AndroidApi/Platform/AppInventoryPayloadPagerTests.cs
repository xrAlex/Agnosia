using Agnosia.Android.Api.Platform;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Platform;

public sealed class AppInventoryPayloadPagerTests
{
    // Проверяет, что helper дробит inventory на страницы с монотонным nextOffset.
    [Fact]
    public void CreatePage_returns_all_items_without_duplicates_across_pages()
    {
        var apps = Enumerable.Range(0, 24)
            .Select(index => Model(index, new string('x', 80)))
            .ToArray();
        var packageNames = new List<string>();
        var offset = 0;
        var previousOffset = -1;

        for (var page = 0; page < 100; page++)
        {
            var result = AppInventoryPayloadPager.CreatePage(apps, offset, 10, 900);

            Assert.True(result.JsonUtf8Bytes <= 900 || result.Apps.Count == 1);
            Assert.True(result.Offset > previousOffset);
            Assert.True(result.NextOffset > result.Offset || !result.HasMore);
            packageNames.AddRange(result.Apps.Select(app => app.PackageName));

            if (!result.HasMore) break;

            previousOffset = result.Offset;
            offset = result.NextOffset;
        }

        Assert.Equal(apps.Select(app => app.PackageName), packageNames);
        Assert.Equal(packageNames.Count, packageNames.Distinct(StringComparer.Ordinal).Count());
    }

    // Проверяет, что один слишком крупный элемент всё равно возвращается и сдвигает offset.
    [Fact]
    public void CreatePage_returns_single_oversized_item_when_it_exceeds_limit()
    {
        var apps = new[]
        {
            Model(0, new string('x', 2048))
        };

        var result = AppInventoryPayloadPager.CreatePage(apps, 0, 10, 128);

        Assert.Single(result.Apps);
        Assert.True(result.JsonUtf8Bytes > 128);
        Assert.Equal(1, result.NextOffset);
        Assert.False(result.HasMore);
    }

    // Проверяет пустой список и offset за пределами inventory.
    [Fact]
    public void CreatePage_returns_empty_terminal_page_for_empty_or_out_of_range_offset()
    {
        var empty = AppInventoryPayloadPager.CreatePage([], 0, 10, 512);
        Assert.Empty(empty.Apps);
        Assert.Equal(0, empty.Offset);
        Assert.Equal(0, empty.NextOffset);
        Assert.False(empty.HasMore);

        var outOfRange = AppInventoryPayloadPager.CreatePage([Model(0, "short")], 10, 10, 512);
        Assert.Empty(outOfRange.Apps);
        Assert.Equal(1, outOfRange.Offset);
        Assert.Equal(1, outOfRange.NextOffset);
        Assert.False(outOfRange.HasMore);
    }

    private static AppServiceModel Model(int index, string labelSuffix)
    {
        return new AppServiceModel
        {
            PackageName = $"org.example.app{index:00}",
            Label = $"Example {index:00} {labelSuffix}",
            SourceDirectory = $"/data/app/org.example.app{index:00}/base.apk",
            SplitApks = [$"/data/app/org.example.app{index:00}/config.arm64.apk"],
            CanLaunch = true,
            IsInstalled = true
        };
    }
}
