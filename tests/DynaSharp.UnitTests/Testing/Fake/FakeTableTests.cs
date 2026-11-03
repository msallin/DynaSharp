using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The reads a query and a scan are built on: sort key order within a partition, both directions, the conditions of a key condition, and where an ExclusiveStartKey goes on.
internal sealed class FakeTableTests
{
    [Test]
    public async Task Range_Forward_GivesTheSortKeysInNumericOrder()
    {
        // Arrange
        var table = Orders(1, 10, 2, 9);

        // Act
        var items = table.Range(null, "c", null, forward: true, exclusiveStart: null);

        // Assert
        await Assert.That(SortKeys(items)).IsEqualTo("1,2,9,10");
    }

    [Test]
    public async Task Range_Backward_GivesTheSortKeysDescending()
    {
        // Arrange
        var table = Orders(1, 10, 2, 9);

        // Act
        var items = table.Range(null, "c", null, forward: false, exclusiveStart: null);

        // Assert
        await Assert.That(SortKeys(items)).IsEqualTo("10,9,2,1");
    }

    [Test]
    [Arguments(SortKeyOperator.Equal, "2", null, "2")]
    [Arguments(SortKeyOperator.LessThan, "9", null, "1,2")]
    [Arguments(SortKeyOperator.LessThanOrEqual, "9", null, "1,2,9")]
    [Arguments(SortKeyOperator.GreaterThan, "2", null, "9,10")]
    [Arguments(SortKeyOperator.GreaterThanOrEqual, "2", null, "2,9,10")]
    [Arguments(SortKeyOperator.Between, "2", "9", "2,9")]
    public async Task Range_SortKeyCondition_GivesWhatItAdmits(SortKeyOperator op, string value, string? upper, string expected)
    {
        // Arrange
        var table = Orders(1, 10, 2, 9);
        var range = new SortKeyRange(op, AttributeValue.FromNumber(value), upper is null ? (AttributeValue?)null : AttributeValue.FromNumber(upper));

        // Act
        var items = table.Range(null, "c", range, forward: true, exclusiveStart: null);

        // Assert
        await Assert.That(SortKeys(items)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(true, "9,10")]
    [Arguments(false, "1")]
    public async Task Range_ExclusiveStart_GoesOnAfterIt(bool forward, string expected)
    {
        // Arrange
        var table = Orders(1, 10, 2, 9);
        var start = new Item { ["pk"] = "c", ["sk"] = 2 };

        // Act
        var items = table.Range(null, "c", null, forward, start);

        // Assert
        await Assert.That(SortKeys(items)).IsEqualTo(expected);
    }

    [Test]
    public async Task Range_PartitionWithoutItems_GivesNothing()
    {
        // Arrange
        var table = Orders(1);

        // Act
        var items = table.Range(null, "other", null, forward: true, exclusiveStart: null);

        // Assert
        await Assert.That(items.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Range_Index_GivesItemsSharingAnIndexKeyByTheirTableKey()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "b", 1, "open");
        Put(table, "a", 2, "open");
        Put(table, "a", 1, "open");
        Put(table, "c", 1, "closed");
        Put(table, "d", 1, null);

        // Act
        var items = table.Range(table.FindIndex("ByStatus"), "open", null, forward: true, exclusiveStart: null);

        // Assert
        await Assert.That(string.Join(",", items.Select(item => $"{item["pk"].AsString()}{item["sk"].AsNumber()}"))).IsEqualTo("a1,a2,b1");
    }

    [Test]
    public async Task Range_IndexWithExclusiveStart_GoesOnAfterTheTableKeyOfIt()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "a", 1, "open");
        Put(table, "a", 2, "open");
        Put(table, "b", 1, "open");
        var start = new Item { ["pk"] = "a", ["sk"] = 2, ["status"] = "open" };

        // Act
        var items = table.Range(table.FindIndex("ByStatus"), "open", null, forward: true, start);

        // Assert
        await Assert.That(string.Join(",", items.Select(item => item["pk"].AsString()))).IsEqualTo("b");
    }

    [Test]
    public async Task ScanAll_ExclusiveStartInALaterPartition_SkipsTheEarlierOnes()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "a", 1, null);
        Put(table, "b", 1, null);
        Put(table, "b", 2, null);
        Put(table, "c", 1, null);
        var start = new Item { ["pk"] = "b", ["sk"] = 1 };

        // Act
        var items = table.ScanAll(null, start, 0, 1);

        // Assert
        await Assert.That(string.Join(",", items.Select(item => $"{item["pk"].AsString()}{item["sk"].AsNumber()}"))).IsEqualTo("b2,c1");
    }

    [Test]
    public async Task ScanAll_OneSegment_GivesEveryItemInKeyOrder()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "b", 1, null);
        Put(table, "a", 2, null);
        Put(table, "a", 1, null);

        // Act
        var items = table.ScanAll(null, null, 0, 1);

        // Assert
        await Assert.That(string.Join(",", items.Select(item => $"{item["pk"].AsString()}{item["sk"].AsNumber()}"))).IsEqualTo("a1,a2,b1");
    }

    [Test]
    public async Task ScanAll_ExclusiveStart_GoesOnAfterIt()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "a", 1, null);
        Put(table, "a", 2, null);
        Put(table, "b", 1, null);
        Put(table, "c", 1, null);
        var start = new Item { ["pk"] = "a", ["sk"] = 2 };

        // Act
        var items = table.ScanAll(null, start, 0, 1);

        // Assert
        await Assert.That(string.Join(",", items.Select(item => item["pk"].AsString()))).IsEqualTo("b,c");
    }

    [Test]
    public async Task ScanAll_Segments_GiveEachItemExactlyOnce()
    {
        // Arrange
        var table = WithStatusIndex();
        for (var i = 0; i < 40; i++)
        {
            Put(table, $"p{i}", 1, null);
        }

        // Act
        var seen = Enumerable.Range(0, 4).SelectMany(segment => table.ScanAll(null, null, segment, 4)).Select(item => item["pk"].AsString()).ToList();

        // Assert
        await Assert.That((seen.Count, seen.Distinct().Count())).IsEqualTo((40, 40));
    }

    [Test]
    public async Task ScanAll_Index_GivesTheProjectedItems()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "a", 1, "open");
        Put(table, "b", 1, null);

        // Act
        var items = table.ScanAll(table.FindIndex("ByStatus"), null, 0, 1).ToList();

        // Assert
        await Assert.That((items.Count, items[0].ContainsKey("note"))).IsEqualTo((1, false));
    }

    [Test]
    public async Task Put_IndexKeyOfTheWrongType_ThrowsAndStoresNothing()
    {
        // Arrange
        var table = WithStatusIndex();

        // Act
        var act = () => table.Put(new Item { ["pk"] = "a", ["sk"] = 1, ["status"] = 5 });

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("One or more parameter values were invalid: Type mismatch for Index Key status Expected: S Actual: N IndexName: ByStatus", StringComparison.Ordinal);
    }

    [Test]
    public async Task Put_ItemOver400Kilobytes_ThrowsValidation()
    {
        // Arrange
        var table = WithStatusIndex();

        // Act
        var act = () => table.Put(new Item { ["pk"] = "a", ["sk"] = 1, ["blob"] = new byte[ItemSize.MaxItemBytes] });

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Item size has exceeded the maximum allowed size", StringComparison.Ordinal);
    }

    [Test]
    public async Task Put_ChangeOfIndexKey_MovesTheItemInTheIndex()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "a", 1, "open");

        // Act
        var old = table.Put(new Item { ["pk"] = "a", ["sk"] = 1, ["status"] = "closed" });

        // Assert
        var index = table.FindIndex("ByStatus")!;
        await Assert.That((old!["status"].AsString(), table.Range(index, "open", null, true, null).Count(), table.Range(index, "closed", null, true, null).Count(), index.ItemCount))
            .IsEqualTo(("open", 0, 1, 1L));
    }

    [Test]
    public async Task Remove_StoredAndMissingItem_ReturnsTheOldImageOrNull()
    {
        // Arrange
        var table = WithStatusIndex();
        Put(table, "a", 1, "open");
        var key = new ItemKey("a", AttributeValue.FromNumber("1"));

        // Act
        var first = table.Remove(key);
        var second = table.Remove(key);

        // Assert
        await Assert.That((first is not null, second is null, table.FindIndex("ByStatus")!.ItemCount, table.ItemCount)).IsEqualTo((true, true, 0L, 0L));
    }

    [Test]
    public async Task FindIndex_LocalAndMissing_FindsTheLocalOne()
    {
        // Arrange
        var table = WithStatusIndex();
        table.LocalIndexes.Add(new FakeIndex("ByNote", new KeySchema("pk", ScalarAttributeType.String, "note", ScalarAttributeType.String), new IndexProjection { ProjectionType = ProjectionType.All }, false, table.KeySchema));

        // Act
        var found = (table.FindIndex("ByNote")?.IsGlobal, table.FindIndex("Missing"));

        // Assert
        await Assert.That(found).IsEqualTo(((bool?)false, (FakeIndex?)null));
    }

    [Test]
    public async Task Put_StoredItem_IsACopyThatCannotChange()
    {
        // Arrange
        var table = WithStatusIndex();
        var item = new Item { ["pk"] = "a", ["sk"] = 1 };
        table.Put(item);

        // Act
        item["note"] = "changed";
        var stored = table.Get(new ItemKey("a", AttributeValue.FromNumber("1")))!;
        var act = () => stored["note"] = "x";

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
    }

    private static FakeTable Orders(params int[] sortKeys)
    {
        var table = new FakeTable("Orders", new KeySchema("pk", ScalarAttributeType.String, "sk", ScalarAttributeType.Number), [], DateTimeOffset.UnixEpoch);
        foreach (var sortKey in sortKeys)
        {
            table.Put(new Item { ["pk"] = "c", ["sk"] = sortKey });
        }

        return table;
    }

    private static FakeTable WithStatusIndex()
    {
        var table = Orders();
        table.GlobalIndexes.Add(new FakeIndex("ByStatus", new KeySchema("status", ScalarAttributeType.String), new IndexProjection { ProjectionType = ProjectionType.KeysOnly }, true, table.KeySchema));
        return table;
    }

    private static void Put(FakeTable table, string pk, int sk, string? status)
    {
        var item = new Item { ["pk"] = pk, ["sk"] = sk, ["note"] = "n" };
        if (status is not null)
        {
            item["status"] = status;
        }

        table.Put(item);
    }

    private static string SortKeys(IEnumerable<Item> items) => string.Join(",", items.Select(item => item["sk"].AsNumber()));
}
