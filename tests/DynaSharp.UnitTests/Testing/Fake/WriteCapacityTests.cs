using System.Buffers;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The write half of Capacity. Oracles from the Developer Guide: "Capacity unit consumption for write operations" (1 unit per KB of the larger image, rounded up) and
// "Provisioned throughput considerations for global secondary indexes" (what an index pays for a write).
public sealed class WriteCapacityTests
{
    private static readonly KeySchema TableKey = new("pk", ScalarAttributeType.String, "sk", ScalarAttributeType.String);

    [Test]
    [Arguments(0L, 0L, 1.0)]
    [Arguments(1L, 0L, 1.0)]
    [Arguments(1024L, 0L, 1.0)]
    [Arguments(1025L, 0L, 2.0)]
    [Arguments(100L, 2500L, 3.0)]
    [Arguments(3000L, 100L, 3.0)]
    [Arguments(0L, 400L * 1024, 400.0)]
    public async Task WriteUnits_Sizes_CostOneUnitPerKilobyteOfTheLargerImage(long oldSize, long newSize, double units)
    {
        // Arrange
        // Act
        var actual = WriteCapacity.Units(oldSize, newSize);

        // Assert
        await Assert.That(actual).IsEqualTo(units);
    }

    [Test]
    public async Task WriteUnits_Outcome_MeasuresBothImages()
    {
        // Arrange: the new image is pk (2 + 1) + sk (2 + 1) + v (1 + 2000) = 2007 bytes
        var key = TableKey.KeyOf(Item("a", "b"));
        var outcome = new WriteOutcome(key, Item("a", "b"), Item("a", "b", ("v", new string('x', 2000))));

        // Act
        var units = WriteCapacity.Units(outcome);

        // Assert
        await Assert.That(units).IsEqualTo(2.0);
    }

    [Test]
    public async Task WriteUnits_DeleteOfNothing_CostsOneUnit()
    {
        // Arrange
        var outcome = new WriteOutcome(TableKey.KeyOf(Item("a", "b")), null, null);

        // Act
        var units = WriteCapacity.Units(outcome);

        // Assert
        await Assert.That(units).IsEqualTo(1.0);
    }

    [Test]
    public async Task IndexWriteUnits_ItemComesIntoBothIndexes_PaysOneWriteEach()
    {
        // Arrange
        var table = Forum();

        // Act
        var units = WriteCapacity.IndexWriteUnits(table, null, Item("a", "b", ("author", "ann"), ("date", "2026")));

        // Assert
        await Assert.That(units).IsEquivalentTo([new IndexUnits("ByAuthor", true, 1), new IndexUnits("ByDate", false, 1)]);
    }

    [Test]
    public async Task IndexWriteUnits_ItemWithoutIndexKeys_PaysNothing()
    {
        // Arrange
        var table = Forum();

        // Act
        var units = WriteCapacity.IndexWriteUnits(table, Item("a", "b"), Item("a", "b", ("v", "x")));

        // Assert
        await Assert.That(units).IsEmpty();
    }

    [Test]
    public async Task IndexWriteUnits_IndexKeyChanges_PaysTheDeleteAndThePut()
    {
        // Arrange
        var table = Forum();

        // Act
        var units = WriteCapacity.IndexWriteUnits(table, Item("a", "b", ("author", "ann")), Item("a", "b", ("author", "bob")));

        // Assert
        await Assert.That(units).IsEquivalentTo([new IndexUnits("ByAuthor", true, 2)]);
    }

    [Test]
    public async Task IndexWriteUnits_ItemLeavesTheIndex_PaysOneWrite()
    {
        // Arrange
        var table = Forum();

        // Act
        var units = WriteCapacity.IndexWriteUnits(table, Item("a", "b", ("date", "2026")), null);

        // Assert
        await Assert.That(units).IsEquivalentTo([new IndexUnits("ByDate", false, 1)]);
    }

    [Test]
    public async Task IndexWriteUnits_ProjectedAttributeChanges_PaysOneWriteOfTheLargerEntry()
    {
        // Arrange: ByAuthor projects ALL, so v is in its entry; the new entry is about 1.5 KB
        var table = Forum();

        // Act
        var units = WriteCapacity.IndexWriteUnits(table, Item("a", "b", ("author", "ann"), ("v", "x")), Item("a", "b", ("author", "ann"), ("v", new string('x', 1500))));

        // Assert
        await Assert.That(units).IsEquivalentTo([new IndexUnits("ByAuthor", true, 2)]);
    }

    [Test]
    public async Task IndexWriteUnits_AttributeOutsideTheProjectionChanges_PaysNothing()
    {
        // Arrange: ByDate is KEYS_ONLY, so v is not in its entry
        var table = Forum();

        // Act
        var units = WriteCapacity.IndexWriteUnits(table, Item("a", "b", ("date", "2026"), ("v", "x")), Item("a", "b", ("date", "2026"), ("v", "y")));

        // Assert
        await Assert.That(units).IsEmpty();
    }

    [Test]
    public async Task Write_Total_ReportsTheUnitsOfTheTableAndItsIndexesAsOneNumber()
    {
        // Arrange
        // Act
        var json = Render(writer => WriteCapacity.Write(writer, ReturnConsumedCapacity.Total, "Forum", 1, [new IndexUnits("ByAuthor", true, 2)]));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"Forum","CapacityUnits":3}}""", json)).IsTrue();
    }

    [Test]
    public async Task Write_Indexes_ReportsTheShareOfTheTableAndOfEachIndex()
    {
        // Arrange
        // Act
        var json = Render(writer => WriteCapacity.Write(writer, ReturnConsumedCapacity.Indexes, "Forum", 0.5, [new IndexUnits("ByAuthor", true, 1), new IndexUnits("ByDate", false, 0.5)]));

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"ConsumedCapacity":{"TableName":"Forum","CapacityUnits":2,"Table":{"CapacityUnits":0.5},
             "LocalSecondaryIndexes":{"ByDate":{"CapacityUnits":0.5}},"GlobalSecondaryIndexes":{"ByAuthor":{"CapacityUnits":1}}}}
            """,
            json)).IsTrue();
    }

    [Test]
    public async Task Write_IndexesWithoutIndexUnits_ReportsTheTableShareAlone()
    {
        // Arrange
        // Act
        var json = Render(writer => WriteCapacity.Write(writer, ReturnConsumedCapacity.Indexes, "Forum", 1, []));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"Forum","CapacityUnits":1,"Table":{"CapacityUnits":1}}}""", json)).IsTrue();
    }

    [Test]
    public async Task WriteAll_SharesOfTwoTables_SumsEachTableAndEachIndex()
    {
        // Arrange
        CapacityShare[] shares =
        [
            new("Forum", 2, [new IndexUnits("ByAuthor", true, 2)]),
            new("Thread", 2, []),
            new("Forum", 4, [new IndexUnits("ByAuthor", true, 2), new IndexUnits("ByDate", false, 2)]),
        ];

        // Act
        var json = Render(writer => WriteCapacity.WriteAll(writer, ReturnConsumedCapacity.Indexes, shares));

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"ConsumedCapacity":[
              {"TableName":"Forum","CapacityUnits":12,"Table":{"CapacityUnits":6},
               "LocalSecondaryIndexes":{"ByDate":{"CapacityUnits":2}},"GlobalSecondaryIndexes":{"ByAuthor":{"CapacityUnits":4}}},
              {"TableName":"Thread","CapacityUnits":2,"Table":{"CapacityUnits":2}}]}
            """,
            json)).IsTrue();
    }

    [Test]
    [Arguments("Read", "ReadCapacityUnits")]
    [Arguments("Write", "WriteCapacityUnits")]
    public async Task WriteAll_Split_CopiesTheUnitsIntoReadOrWrite(string split, string member)
    {
        // Arrange
        CapacityShare[] shares = [new("Forum", 4, [new IndexUnits("ByAuthor", true, 2), new IndexUnits("ByDate", false, 1)])];

        // Act
        var json = Render(writer => WriteCapacity.WriteAll(writer, ReturnConsumedCapacity.Indexes, shares, Enum.Parse<WriteCapacity.Split>(split)));

        // Assert
        await Assert.That(FakeCalls.SameJson(
            $$$$"""
            {"ConsumedCapacity":[
              {"TableName":"Forum","CapacityUnits":7,"{{{{member}}}}":7,"Table":{"CapacityUnits":4,"{{{{member}}}}":4},
               "LocalSecondaryIndexes":{"ByDate":{"CapacityUnits":1,"{{{{member}}}}":1}},"GlobalSecondaryIndexes":{"ByAuthor":{"CapacityUnits":2,"{{{{member}}}}":2}}}]}
            """,
            json)).IsTrue();
    }

    [Test]
    public async Task WriteAll_TotalWithASplit_ReportsCapacityUnitsAlone()
    {
        // Arrange
        CapacityShare[] shares = [new("Forum", 4, [new IndexUnits("ByAuthor", true, 2)])];

        // Act
        var json = Render(writer => WriteCapacity.WriteAll(writer, ReturnConsumedCapacity.Total, shares, WriteCapacity.Split.Write));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":[{"TableName":"Forum","CapacityUnits":6}]}""", json)).IsTrue();
    }

    [Test]
    public async Task OfWrite_InATransaction_DoublesTheTableAndTheIndexes()
    {
        // Arrange
        var table = Forum();
        var item = Item("a", "b", ("author", "ann"));

        // Act
        var share = CapacityShare.OfWrite(table, new WriteOutcome(TableKey.KeyOf(item), null, item), factor: 2);

        // Assert
        await Assert.That((share.TableName, share.TableUnits, share.Indexes.Single())).IsEqualTo(("Forum", 2.0, new IndexUnits("ByAuthor", true, 2)));
    }

    // A table keyed by pk and sk, with a global index ByAuthor (ALL) and a local index ByDate (KEYS_ONLY).
    internal static FakeTable Forum()
    {
        var table = new FakeTable("Forum", TableKey, [], DateTimeOffset.UnixEpoch);
        table.GlobalIndexes.Add(new FakeIndex("ByAuthor", new KeySchema("author", ScalarAttributeType.String), new IndexProjection { ProjectionType = ProjectionType.All }, isGlobal: true, TableKey));
        table.LocalIndexes.Add(
            new FakeIndex("ByDate", new KeySchema("pk", ScalarAttributeType.String, "date", ScalarAttributeType.String), new IndexProjection { ProjectionType = ProjectionType.KeysOnly }, isGlobal: false, TableKey));
        return table;
    }

    internal static Item Item(string pk, string sk, params (string Name, string Value)[] attributes)
    {
        var item = new Item { ["pk"] = AttributeValue.FromString(pk), ["sk"] = AttributeValue.FromString(sk) };
        foreach (var (name, value) in attributes)
        {
            item[name] = AttributeValue.FromString(value);
        }

        return item;
    }

    internal static JsonElement Render(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        return JsonElement.Parse(buffer.WrittenSpan);
    }
}
