using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Autocomplete</see>: suggestions under a sort key in a folded form, and <c>begins_with</c> on what was typed so far, with a <c>Limit</c> for the first few. A key is never folded for you, so the entity stores the
/// folded text beside the display text. A prefix everyone types needs write shards, and then a fan-out merges them back into order.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class AutocompleteTests(Backend backend)
{
    private const int Shards = 3;

    [Test]
    public async Task Query_WhatWasTypedSoFar_GivesTheFirstMatches()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var cities = sandbox.Shop.Of<CityName>();
        string[] names = ["Zurich", "Zug", "Zuoz", "Zermatt", "Aarau"];
        await cities.BatchWrite().PutAll(names.Select(static city => new CityName { Country = "CH", Folded = city.ToUpperInvariant(), Display = city })).SendAsync();

        // Act
        var typed = "zu".ToUpperInvariant();
        var page = await cities.Query(CityName.Keys.Pk.Of("CH"), SortKeyCondition.BeginsWith(CityName.Keys.Sk.PrefixPartial(typed))).Limit(2).PageAsync();

        // Assert
        await Assert.That(string.Join(",", page.Items.Select(static city => city.Display))).IsEqualTo("Zug,Zuoz");
    }

    [Test]
    public async Task PageAsync_ShardsOfAHotPrefix_MergeBackIntoOrder()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var terms = sandbox.Shop.Of<SearchTerm>();
        string[] hot = ["iphone", "iphone case", "iphone charger", "iphone pro", "ipad", "ipod"];
        await terms.BatchWrite().PutAll(hot.Select(static (term, i) => new SearchTerm { Prefix = "ip", Shard = i % Shards, Term = term })).SendAsync();
        var shards = Enumerable.Range(0, Shards).Select(static shard => SearchTerm.Keys.Pk.Of("ip", shard));

        // Act
        var page = await terms.Query(shards, SortKeyCondition.BeginsWith(SearchTerm.Keys.Sk.PrefixPartial("iph"))).PageAsync(3);

        // Assert
        await Assert.That(string.Join(",", page.Items.Select(static term => term.Term))).IsEqualTo("iphone,iphone case,iphone charger");
    }
}

[DynamoDbEntity]
[Discriminator("CITY")]
[PartitionKey("CITIES#{Country}")]
[SortKey("{Folded}")]
internal sealed partial record CityName
{
    public required string Country { get; init; }

    public required string Folded { get; init; }

    public required string Display { get; init; }
}

[DynamoDbEntity]
[Discriminator("SEARCH_TERM")]
[PartitionKey("TERMS#{Prefix}#{Shard:d1}")]
[SortKey("{Term}")]
internal sealed partial record SearchTerm
{
    public required string Prefix { get; init; }

    public required int Shard { get; init; }

    public required string Term { get; init; }
}
