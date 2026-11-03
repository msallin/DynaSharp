using System.Buffers;
using System.Text.Json;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The arithmetic of the Developer Guide, "Read consistency" and "Capacity unit consumption for reads": 1 unit per 4 KB rounded up, at least 1, half for an eventually consistent
// read. The bodies are those of the examples of the AWS CLI reference for get-item and query with --return-consumed-capacity.
public sealed class ReadCapacityTests
{
    [Test]
    [Arguments(0L, false, 0.5)]
    [Arguments(0L, true, 1.0)]
    [Arguments(1L, false, 0.5)]
    [Arguments(4096L, true, 1.0)]
    [Arguments(4097L, true, 2.0)]
    [Arguments(6144L, true, 2.0)]
    [Arguments(6144L, false, 1.0)]
    [Arguments(41779L, true, 11.0)]
    [Arguments(41779L, false, 5.5)]
    [Arguments(1048576L, false, 128.0)]
    public async Task Units_Size_IsOnePerFourKilobytesRoundedUp(long size, bool consistent, double expected)
    {
        // Arrange
        // Act
        var units = ReadCapacity.Units(size, consistent);

        // Assert
        await Assert.That(units).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_None_WritesNothing()
    {
        // Arrange
        // Act
        var body = Render(json => ReadCapacity.Write(json, ReturnConsumedCapacity.None, "Orders", 1, null));

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    public async Task Write_TotalOfATable_WritesTheTableAndItsUnits()
    {
        // Arrange
        // Act
        var body = Render(json => ReadCapacity.Write(json, ReturnConsumedCapacity.Total, "MusicCollection", 0.5, null));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"MusicCollection","CapacityUnits":0.5}}""", body)).IsTrue();
    }

    [Test]
    public async Task Write_TotalOfAnIndex_AddsTheUnitsUp()
    {
        // Arrange
        // Act
        var body = Render(json => ReadCapacity.Write(json, ReturnConsumedCapacity.Total, "MusicCollection", 1, new IndexUnits("AlbumTitleIndex", false, 0.5)));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"MusicCollection","CapacityUnits":1.5}}""", body)).IsTrue();
    }

    [Test]
    public async Task Write_IndexesOfATable_ReportsTheTableShare()
    {
        // Arrange
        // Act
        var body = Render(json => ReadCapacity.Write(json, ReturnConsumedCapacity.Indexes, "MusicCollection", 1, null));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"MusicCollection","CapacityUnits":1.0,"Table":{"CapacityUnits":1.0}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Write_IndexesOfALocalIndex_ReportsTheIndexShareAndNoneOfTheTable()
    {
        // Arrange
        // Act
        var body = Render(json => ReadCapacity.Write(json, ReturnConsumedCapacity.Indexes, "MusicCollection", 0, new IndexUnits("AlbumTitleIndex", false, 0.5)));

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """{"ConsumedCapacity":{"TableName":"MusicCollection","CapacityUnits":0.5,"Table":{"CapacityUnits":0.0},"LocalSecondaryIndexes":{"AlbumTitleIndex":{"CapacityUnits":0.5}}}}""",
            body)).IsTrue();
    }

    [Test]
    public async Task Write_IndexesOfAGlobalIndex_ReportsTheIndexShare()
    {
        // Arrange
        // Act
        var body = Render(json => ReadCapacity.Write(json, ReturnConsumedCapacity.Indexes, "Orders", 0, new IndexUnits("ByCustomer", true, 1)));

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """{"ConsumedCapacity":{"TableName":"Orders","CapacityUnits":1.0,"Table":{"CapacityUnits":0.0},"GlobalSecondaryIndexes":{"ByCustomer":{"CapacityUnits":1.0}}}}""",
            body)).IsTrue();
    }

    private static JsonElement Render(Action<Utf8JsonWriter> write)
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
