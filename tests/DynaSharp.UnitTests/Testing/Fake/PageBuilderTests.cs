using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The page of the Developer Guide, "Paginating table query results", "Limiting the number of items in the result set" and "Working with local secondary indexes": Limit counts the
// items read, the page ends at 1 MB read, the filter comes after both, a key comes back when the page was cut, and a local index fetches from its table what it does not hold.
public sealed class PageBuilderTests
{
    private static readonly KeySchema TableKey = new("pk", ScalarAttributeType.String, "sk", ScalarAttributeType.Number);

    [Test]
    public async Task Build_NoLimit_ReadsEverythingAndEndsWithoutAKey()
    {
        // Arrange
        var table = Orders(5);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table));

        // Assert
        await Assert.That((Keys(page), page.Count, page.ScannedCount, page.LastEvaluatedKey)).IsEqualTo(("1,2,3,4,5", 5, 5, (Item?)null));
    }

    [Test]
    public async Task Build_LimitBelowTheItems_EndsWithTheKeyOfTheLastItemRead()
    {
        // Arrange
        var table = Orders(5);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Limit = 2 });

        // Assert
        await Assert.That((Keys(page), page.LastEvaluatedKey)).IsEqualTo(("1,2", (Item?)new Item { ["pk"] = "c", ["sk"] = 2 }));
    }

    [Test]
    public async Task Build_LimitOfAllTheItems_StillEndsWithAKey()
    {
        // Arrange: DynamoDB does not look past the Limit, so a key does not promise more items
        var table = Orders(5);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Limit = 5 });

        // Assert
        await Assert.That((page.ScannedCount, page.LastEvaluatedKey)).IsEqualTo((5, (Item?)new Item { ["pk"] = "c", ["sk"] = 5 }));
    }

    [Test]
    public async Task Build_LimitAboveTheItems_EndsWithoutAKey()
    {
        // Arrange
        var table = Orders(5);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Limit = 6 });

        // Assert
        await Assert.That((page.ScannedCount, page.LastEvaluatedKey)).IsEqualTo((5, (Item?)null));
    }

    [Test]
    public async Task Build_FilterThatDropsEverythingRead_ReturnsAnEmptyPageWithAKey()
    {
        // Arrange
        var table = Orders(5);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Limit = 2, Filter = static item => item["v"].AsNumber<int>() > 4 });

        // Assert
        await Assert.That((page.Items.Count, page.Count, page.ScannedCount, page.LastEvaluatedKey is not null)).IsEqualTo((0, 0, 2, true));
    }

    [Test]
    public async Task Build_Filter_CountsWhatPassesAndWhatWasRead()
    {
        // Arrange
        var table = Orders(5);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Filter = static item => item["v"].AsNumber<int>() >= 4 });

        // Assert
        await Assert.That((Keys(page), page.Count, page.ScannedCount)).IsEqualTo(("4,5", 2, 5));
    }

    [Test]
    public async Task Build_SelectCount_CountsWithoutItems()
    {
        // Arrange
        var table = Orders(4);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Select = SelectKind.Count });

        // Assert
        await Assert.That((page.Items.Count, page.Count, page.ScannedCount)).IsEqualTo((0, 4, 4));
    }

    [Test]
    [Arguments(SelectKind.Unspecified)]
    [Arguments(SelectKind.SpecificAttributes)]
    public async Task Build_Projection_ReturnsWhatItTakesOfEachItem(SelectKind select)
    {
        // Arrange
        var table = Orders(2);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Select = select, Projection = Only("v") });

        // Assert
        await Assert.That(page.Items).IsEquivalentTo([new Item { ["v"] = 1 }, new Item { ["v"] = 2 }]);
    }

    [Test]
    [Arguments(SelectKind.Unspecified)]
    [Arguments(SelectKind.AllAttributes)]
    public async Task Build_TableWithoutProjection_ReturnsWholeItems(SelectKind select)
    {
        // Arrange
        var table = Orders(1);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { Select = select });

        // Assert
        await Assert.That(page.Items[0]).IsEqualTo(Order(1));
    }

    [Test]
    public async Task Build_GlobalIndexWithLimit_KeyHoldsTheIndexKeyAndTheTableKey()
    {
        // Arrange
        var table = Orders(3);
        var index = table.FindIndex("ByCustomer");

        // Act
        var page = PageBuilder.Build(table.Range(index, "k", null, forward: true, exclusiveStart: null), Options(table, index) with { Limit = 1 });

        // Assert
        await Assert.That(page.LastEvaluatedKey).IsEqualTo(new Item { ["customer"] = "k", ["pk"] = "c", ["sk"] = 1 });
    }

    [Test]
    public async Task Build_LocalIndexWithLimit_KeyHoldsTheIndexSortKeyAndTheTableKey()
    {
        // Arrange
        var table = Orders(3);
        var index = table.FindIndex("ByStatus");

        // Act
        var page = PageBuilder.Build(table.Range(index, "c", null, forward: true, exclusiveStart: null), Options(table, index) with { Limit = 1 });

        // Assert
        await Assert.That(page.LastEvaluatedKey).IsEqualTo(new Item { ["pk"] = "c", ["sk"] = 1, ["status"] = "s1" });
    }

    [Test]
    [Arguments(0, 3)]
    [Arguments(-1, 4)]
    public async Task Build_ItemsAroundOneMegabyte_EndTheReadWhereTheyReachIt(int lastPadDelta, int scanned)
    {
        // Arrange: 349526 + 349525 + 349525 bytes are 1 MB exactly, so the third item ends the page; one byte less, and the fourth item does
        var table = new FakeTable("Orders", TableKey, [], DateTimeOffset.UnixEpoch);
        int[] sizes = [349526, 349525, 349525 + lastPadDelta, 100];
        for (var i = 0; i < sizes.Length; i++)
        {
            // "pk":"c" is 3 bytes, "sk":N one digit 4, "p" 1, and the string the rest
            _ = table.Put(new Item { ["pk"] = "c", ["sk"] = i + 1, ["p"] = new string('x', sizes[i] - 8) });
        }

        // Act
        var page = PageBuilder.Build(Read(table), Options(table));

        // Assert
        await Assert.That((page.ScannedCount, page.LastEvaluatedKey?["sk"].AsNumber<int>())).IsEqualTo((scanned, (int?)scanned));
    }

    [Test]
    [Arguments(false, 0.5)]
    [Arguments(true, 1.0)]
    public async Task Build_SmallItems_CostOneReadOfFourKilobytes(bool consistent, double units)
    {
        // Arrange
        var table = Orders(5);

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { ConsistentRead = consistent });

        // Assert
        await Assert.That((page.Units, page.FetchUnits)).IsEqualTo((units, 0.0));
    }

    [Test]
    public async Task Build_FilteredItems_CostWhatWasRead()
    {
        // Arrange: three items of about 3 KB are read, 9 KB, three units of 4 KB, though none passes the filter
        var table = new FakeTable("Orders", TableKey, [], DateTimeOffset.UnixEpoch);
        for (var i = 1; i <= 3; i++)
        {
            _ = table.Put(new Item { ["pk"] = "c", ["sk"] = i, ["p"] = new string('x', 3000) });
        }

        // Act
        var page = PageBuilder.Build(Read(table), Options(table) with { ConsistentRead = true, Filter = static _ => false });

        // Assert
        await Assert.That((page.Count, page.Units)).IsEqualTo((0, 3.0));
    }

    [Test]
    public async Task Build_IndexWithoutSelect_ReturnsWhatTheIndexProjects()
    {
        // Arrange
        var table = Orders(1);
        var index = table.FindIndex("ByCustomer");

        // Act
        var page = PageBuilder.Build(table.Range(index, "k", null, forward: true, exclusiveStart: null), Options(table, index));

        // Assert
        await Assert.That(page.Items[0]).IsEqualTo(new Item { ["pk"] = "c", ["sk"] = 1, ["customer"] = "k" });
    }

    [Test]
    public async Task Build_AllAttributesOfAGlobalIndexOfAll_ReturnsWholeItems()
    {
        // Arrange
        var table = Orders(1);
        var index = table.FindIndex("Everything");

        // Act
        var page = PageBuilder.Build(table.Range(index, "k", null, forward: true, exclusiveStart: null), Options(table, index) with { Select = SelectKind.AllAttributes });

        // Assert
        await Assert.That((page.Items[0], page.FetchUnits)).IsEqualTo((Order(1), 0.0));
    }

    [Test]
    public async Task Build_AllAttributesOfALocalIndexOfAll_ReadsTheIndexAlone()
    {
        // Arrange
        var table = Orders(2);
        var index = table.FindIndex("ByStatusAll");

        // Act
        var page = PageBuilder.Build(table.Range(index, "c", null, forward: true, exclusiveStart: null), Options(table, index) with { Select = SelectKind.AllAttributes });

        // Assert
        await Assert.That((page.Items[0], page.FetchUnits)).IsEqualTo((Order(1), 0.0));
    }

    [Test]
    public async Task Build_AllAttributesOfALocalIndexOfKeys_FetchesEachItemFromTheTable()
    {
        // Arrange: each fetch costs the units of its item, half a unit each for an eventually consistent read
        var table = Orders(3);
        var index = table.FindIndex("ByStatus");

        // Act
        var page = PageBuilder.Build(table.Range(index, "c", null, forward: true, exclusiveStart: null), Options(table, index) with { Select = SelectKind.AllAttributes });

        // Assert
        await Assert.That((page.Items[2], page.Units, page.FetchUnits)).IsEqualTo((Order(3), 0.5, 1.5));
    }

    [Test]
    public async Task Build_FilterOnALocalIndexThatFetches_SeesTheWholeItem()
    {
        // Arrange
        var table = Orders(3);
        var index = table.FindIndex("ByStatus");

        // Act
        var page = PageBuilder.Build(
            table.Range(index, "c", null, forward: true, exclusiveStart: null),
            Options(table, index) with { Select = SelectKind.AllAttributes, Filter = static item => item["v"].AsNumber<int>() > 2 });

        // Assert
        await Assert.That((page.Count, page.ScannedCount)).IsEqualTo((1, 3));
    }

    [Test]
    [Arguments(SelectKind.Unspecified)]
    [Arguments(SelectKind.AllProjectedAttributes)]
    public async Task Build_ProjectedAttributesOfALocalIndexOfKeys_ReadsTheIndexAlone(SelectKind select)
    {
        // Arrange
        var table = Orders(1);
        var index = table.FindIndex("ByStatus");

        // Act
        var page = PageBuilder.Build(table.Range(index, "c", null, forward: true, exclusiveStart: null), Options(table, index) with { Select = select });

        // Assert
        await Assert.That((page.Items[0], page.FetchUnits)).IsEqualTo((new Item { ["pk"] = "c", ["sk"] = 1, ["status"] = "s1" }, 0.0));
    }

    [Test]
    public async Task Build_ProjectionOfWhatALocalIndexHolds_ReadsTheIndexAlone()
    {
        // Arrange
        var table = Orders(1);
        var index = table.FindIndex("ByStatus");

        // Act
        var page = PageBuilder.Build(table.Range(index, "c", null, forward: true, exclusiveStart: null), Options(table, index) with { Projection = Only("status") });

        // Assert
        await Assert.That((page.Items[0], page.FetchUnits)).IsEqualTo((new Item { ["status"] = "s1" }, 0.0));
    }

    [Test]
    public async Task Build_ProjectionOfWhatOnlyTheTableHolds_FetchesTheItem()
    {
        // Arrange
        var table = Orders(1);
        var index = table.FindIndex("ByStatus");

        // Act
        var page = PageBuilder.Build(table.Range(index, "c", null, forward: true, exclusiveStart: null), Options(table, index) with { Projection = Only("v") });

        // Assert
        await Assert.That((page.Items[0], page.FetchUnits)).IsEqualTo((new Item { ["v"] = 1 }, 0.5));
    }

    [Test]
    public async Task Build_CountOfALocalIndexOfKeys_FetchesNothing()
    {
        // Arrange
        var table = Orders(2);
        var index = table.FindIndex("ByStatus");

        // Act
        var page = PageBuilder.Build(table.Range(index, "c", null, forward: true, exclusiveStart: null), Options(table, index) with { Select = SelectKind.Count });

        // Assert
        await Assert.That((page.Count, page.FetchUnits)).IsEqualTo((2, 0.0));
    }

    [Test]
    public async Task Build_SpecificAttributesWithoutAProjection_AnswersValidation()
    {
        // Arrange
        var table = Orders(1);

        // Act
        var act = () => PageBuilder.Build(Read(table), Options(table) with { Select = SelectKind.SpecificAttributes });

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("1 validation error detected: Must specify the AttributesToGet or ProjectionExpression when choosing to get SPECIFIC_ATTRIBUTES", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(SelectKind.Count, "only the Count")]
    [Arguments(SelectKind.AllAttributes, "ALL_ATTRIBUTES")]
    [Arguments(SelectKind.AllProjectedAttributes, "ALL_PROJECTED_ATTRIBUTES")]
    public async Task Build_ProjectionWithAnotherSelect_AnswersValidation(SelectKind select, string spelling)
    {
        // Arrange
        var table = Orders(1);

        // Act
        var act = () => PageBuilder.Build(Read(table), Options(table) with { Select = select, Projection = Only("v") });

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage($"Cannot specify the ProjectionExpression when choosing to get {spelling}", StringComparison.Ordinal);
    }

    [Test]
    public async Task Build_AllProjectedAttributesOfATable_AnswersValidation()
    {
        // Arrange
        var table = Orders(1);

        // Act
        var act = () => PageBuilder.Build(Read(table), Options(table) with { Select = SelectKind.AllProjectedAttributes });

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("ALL_PROJECTED_ATTRIBUTES can be used only when Querying using an IndexName", StringComparison.Ordinal);
    }

    [Test]
    public async Task Build_AllAttributesOfAGlobalIndexOfKeys_AnswersValidation()
    {
        // Arrange
        var table = Orders(1);
        var index = table.FindIndex("ByCustomer");

        // Act
        var act = () => PageBuilder.Build([], Options(table, index) with { Select = SelectKind.AllAttributes });

        // Assert
        await Assert.That(act).Throws<FakeError>()
            .WithMessage("One or more parameter values were invalid: Select type ALL_ATTRIBUTES is not supported for global secondary index ByCustomer because its projection type is not ALL", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStartKey_NoMember_IsNull()
    {
        // Arrange
        var table = Orders(0);

        // Act
        var key = PageBuilder.ReadStartKey(JsonElement.Parse("""{"ExclusiveStartKey":null}"""), table, null);

        // Assert
        await Assert.That(key).IsNull();
    }

    [Test]
    [Arguments("""{"pk":{"S":"c"},"sk":{"N":"2.0"}}""", null, """{"pk":{"S":"c"},"sk":{"N":"2"}}""")]
    [Arguments("""{"customer":{"S":"k"},"pk":{"S":"c"},"sk":{"N":"1"}}""", "ByCustomer", """{"customer":{"S":"k"},"pk":{"S":"c"},"sk":{"N":"1"}}""")]
    [Arguments("""{"pk":{"S":"c"},"sk":{"N":"1"},"status":{"S":"s1"}}""", "ByStatus", """{"pk":{"S":"c"},"sk":{"N":"1"},"status":{"S":"s1"}}""")]
    public async Task ReadStartKey_KeyOfTheRead_IsItInItsStoredForm(string key, string? indexName, string expected)
    {
        // Arrange
        var table = Orders(0);
        var index = indexName is null ? null : table.FindIndex(indexName);

        // Act
        var start = PageBuilder.ReadStartKey(JsonElement.Parse($$"""{"ExclusiveStartKey":{{key}}}"""), table, index);

        // Assert
        await Assert.That(start).IsEqualTo(RequestJson.ReadItem(JsonElement.Parse(expected), "expected"));
    }

    [Test]
    [Arguments("{}", null)]
    [Arguments("""{"pk":{"S":"c"}}""", null)]
    [Arguments("""{"pk":{"S":"c"},"sk":{"N":"1"},"v":{"N":"1"}}""", null)]
    [Arguments("""{"pk":{"S":"c"},"sk":{"S":"1"}}""", null)]
    [Arguments("""{"pk":{"S":""},"sk":{"N":"1"}}""", null)]
    [Arguments("""{"pk":{"S":"c"},"sk":{"N":"1"}}""", "ByCustomer")]
    [Arguments("""{"customer":{"N":"1"},"pk":{"S":"c"},"sk":{"N":"1"}}""", "ByCustomer")]
    [Arguments("""{"customer":{"S":"k"},"pk":{"S":"c"},"other":{"N":"1"}}""", "ByCustomer")]
    [Arguments("""{"pk":{"S":"c"},"sk":{"N":"1"},"other":{"S":"s1"}}""", "ByStatus")]
    public async Task ReadStartKey_KeyThatDoesNotFit_AnswersValidation(string key, string? indexName)
    {
        // Arrange
        var table = Orders(0);
        var index = indexName is null ? null : table.FindIndex(indexName);

        // Act
        var act = () => PageBuilder.ReadStartKey(JsonElement.Parse($$"""{"ExclusiveStartKey":{{key}}}"""), table, index);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The provided starting key is invalid", StringComparison.Ordinal);
    }

    // A table of orders of customer "c" with sort keys 1 to count, a local index by status of keys only and one of all, and global indexes by customer of keys only and of all.
    private static FakeTable Orders(int count)
    {
        var table = new FakeTable("Orders", TableKey, [], DateTimeOffset.UnixEpoch);
        var byStatus = new KeySchema("pk", ScalarAttributeType.String, "status", ScalarAttributeType.String);
        var byCustomer = new KeySchema("customer", ScalarAttributeType.String);
        table.LocalIndexes.Add(new FakeIndex("ByStatus", byStatus, new IndexProjection { ProjectionType = ProjectionType.KeysOnly }, false, TableKey));
        table.LocalIndexes.Add(new FakeIndex("ByStatusAll", byStatus, new IndexProjection { ProjectionType = ProjectionType.All }, false, TableKey));
        table.GlobalIndexes.Add(new FakeIndex("ByCustomer", byCustomer, new IndexProjection { ProjectionType = ProjectionType.KeysOnly }, true, TableKey));
        table.GlobalIndexes.Add(new FakeIndex("Everything", byCustomer, new IndexProjection { ProjectionType = ProjectionType.All }, true, TableKey));
        for (var i = 1; i <= count; i++)
        {
            _ = table.Put(Order(i));
        }

        return table;
    }

    private static Item Order(int i) => new() { ["pk"] = "c", ["sk"] = i, ["status"] = $"s{i}", ["customer"] = "k", ["v"] = i };

    private static IEnumerable<Item> Read(FakeTable table) => table.Range(null, "c", null, forward: true, exclusiveStart: null);

    private static PageOptions Options(FakeTable table, FakeIndex? index = null) => new(null, null, null, SelectKind.Unspecified, false, table, index);

    // A projection of top-level attributes, which stands in for a parsed one.
    private static Func<Item, Item> Only(params string[] names) => item =>
    {
        var projected = new Item();
        foreach (var name in names.Where(item.ContainsKey))
        {
            projected[name] = item[name];
        }

        return projected;
    };

    private static string Keys(Page page) => string.Join(",", page.Items.Select(static item => item["sk"].AsNumber<int>()));
}
