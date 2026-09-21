using Agnosia.Android.Packages;
using Xunit;

namespace Agnosia.Unit.Android.Packages;

public sealed class PackageInventoryQueryTests
{
    [Fact]
    public void Keeps_application_inventory_without_querying_known_packages()
    {
        var apps = PackageInventoryQuery.Read<string>(
            () => ["com.agnosia.app", "ru.fourpda.client"],
            TestContext.Current.CancellationToken,
            () => throw new InvalidOperationException("Known package lookup must not run."));

        Assert.Equal(["com.agnosia.app", "ru.fourpda.client"], apps);
    }

    [Fact]
    public void Empty_application_query_is_a_failure_not_an_empty_user_app_list()
    {
        Assert.Throws<PackageInventoryUnavailableException>(() =>
            PackageInventoryQuery.Read<string>(() => [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Empty_application_query_uses_known_package_lookup()
    {
        var lookedUp = false;
        var apps = PackageInventoryQuery.Read<string>(
            () => [],
            TestContext.Current.CancellationToken,
            () =>
            {
                lookedUp = true;
                return ["com.agnosia.app", "ru.fourpda.client"];
            });

        Assert.True(lookedUp);
        Assert.Equal(["com.agnosia.app", "ru.fourpda.client"], apps);
    }

    [Fact]
    public void Known_package_lookup_is_not_used_when_application_query_succeeds()
    {
        var apps = PackageInventoryQuery.Read<string>(
            () => ["com.example.first"],
            TestContext.Current.CancellationToken,
            () => throw new InvalidOperationException("Known package lookup must not run."));

        Assert.Equal("com.example.first", Assert.Single(apps));
    }

    [Fact]
    public void Empty_known_package_lookup_still_reports_inventory_unavailable()
    {
        Assert.Throws<PackageInventoryUnavailableException>(() =>
            PackageInventoryQuery.Read<string>(
                () => [],
                TestContext.Current.CancellationToken,
                () => []));
    }

    [Fact]
    public void Known_package_lookup_checks_each_name_once_and_keeps_only_work_results()
    {
        var requested = new List<string>();
        var apps = PackageInventoryQuery.ReadKnownPackages(
            ["ru.fourpda.client", "personal.only", "ru.fourpda.client", "com.agnosia.app"],
            name =>
            {
                requested.Add(name);
                return name == "personal.only" ? null : name;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["ru.fourpda.client", "personal.only", "com.agnosia.app"], requested);
        Assert.Equal(["ru.fourpda.client", "com.agnosia.app"], apps);
    }

    [Fact]
    public void Known_package_lookup_stops_when_cancelled_between_names()
    {
        using var cancellation = new CancellationTokenSource();
        var requested = new List<string>();

        Assert.Throws<OperationCanceledException>(() => PackageInventoryQuery.ReadKnownPackages(
            ["first", "second"],
            name =>
            {
                requested.Add(name);
                cancellation.Cancel();
                return name;
            },
            cancellation.Token));
        Assert.Equal(["first"], requested);
    }

    [Fact]
    public void Known_package_lookup_failure_does_not_return_a_partial_inventory()
    {
        var requested = new List<string>();

        Assert.Throws<PackageInventoryUnavailableException>(() => PackageInventoryQuery.Read<string>(
            () => [],
            TestContext.Current.CancellationToken,
            () => PackageInventoryQuery.ReadKnownPackages(
                ["first", "second"],
                name =>
                {
                    requested.Add(name);
                    if (name == "second") throw new PackageInventoryUnavailableException();
                    return name;
                },
                TestContext.Current.CancellationToken)));

        Assert.Equal(["first", "second"], requested);
    }

    [Fact]
    public void Inventory_containing_only_agnosia_is_valid_before_user_app_filtering()
    {
        var apps = PackageInventoryQuery.Read<string>(
            () => ["com.agnosia.app"],
            TestContext.Current.CancellationToken);

        Assert.Equal("com.agnosia.app", Assert.Single(apps));
    }

    [Fact]
    public void Cancellation_prevents_any_query()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => PackageInventoryQuery.Read<string>(
            () => throw new InvalidOperationException("Query must not run."),
            cancellation.Token));
    }

    [Fact]
    public void Cancellation_between_application_and_known_package_queries_prevents_fallback()
    {
        using var cancellation = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() => PackageInventoryQuery.Read<string>(
            () =>
            {
                cancellation.Cancel();
                return [];
            },
            cancellation.Token,
            () => throw new InvalidOperationException("Known package lookup must not run.")));
    }
}
