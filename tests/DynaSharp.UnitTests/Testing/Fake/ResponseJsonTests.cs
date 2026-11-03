using System.Buffers;
using System.Text.Json;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// ConsumedCapacity and CancellationReasons in the shapes of the API reference: only what is set, indexes by name.
public sealed class ResponseJsonTests
{
    [Test]
    public async Task WriteItems_TwoItems_WritesAnArray()
    {
        // Arrange
        Item[] items = [new() { ["pk"] = "a" }, new() { ["pk"] = "b", ["n"] = 1 }];

        // Act
        var json = Render(writer => ResponseJson.WriteItems(writer, "Items", items));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Items":[{"pk":{"S":"a"}},{"pk":{"S":"b"},"n":{"N":"1"}}]}""", json)).IsTrue();
    }

    [Test]
    public async Task WriteConsumedCapacity_Everything_WritesEveryPart()
    {
        // Arrange
        var capacity = new ConsumedCapacity
        {
            TableName = "Orders",
            CapacityUnits = 3,
            ReadCapacityUnits = 1,
            WriteCapacityUnits = 2,
            Table = new CapacityDetail { CapacityUnits = 1 },
            LocalSecondaryIndexes = new Dictionary<string, CapacityDetail> { ["ByDate"] = new() { ReadCapacityUnits = 0.5 } },
            GlobalSecondaryIndexes = new Dictionary<string, CapacityDetail> { ["ByCustomer"] = new() { WriteCapacityUnits = 1.5 } },
        };

        // Act
        var json = Render(writer =>
        {
            writer.WritePropertyName("ConsumedCapacity");
            ResponseJson.WriteConsumedCapacity(writer, capacity);
        });

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"ConsumedCapacity":{"TableName":"Orders","CapacityUnits":3,"ReadCapacityUnits":1,"WriteCapacityUnits":2,"Table":{"CapacityUnits":1},
             "LocalSecondaryIndexes":{"ByDate":{"ReadCapacityUnits":0.5}},"GlobalSecondaryIndexes":{"ByCustomer":{"WriteCapacityUnits":1.5}}}}
            """,
            json)).IsTrue();
    }

    [Test]
    public async Task WriteConsumedCapacity_Nothing_WritesAnEmptyObject()
    {
        // Arrange
        var capacity = new ConsumedCapacity();

        // Act
        var json = Render(writer =>
        {
            writer.WritePropertyName("ConsumedCapacity");
            ResponseJson.WriteConsumedCapacity(writer, capacity);
        });

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{}}""", json)).IsTrue();
    }

    [Test]
    public async Task WriteCancellationReasons_Reasons_WritesThemInOrder()
    {
        // Arrange
        CancellationReason[] reasons = [new() { Code = "None" }, new() { Code = "ConditionalCheckFailed", Message = "The conditional request failed" }];

        // Act
        var json = Render(writer => ResponseJson.WriteCancellationReasons(writer, reasons));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"CancellationReasons":[{"Code":"None"},{"Code":"ConditionalCheckFailed","Message":"The conditional request failed"}]}""", json)).IsTrue();
    }

    [Test]
    public async Task KeyElements_TableWithASortKey_GivesHashThenRange()
    {
        // Arrange
        var keySchema = new KeySchema("pk", global::DynaSharp.Requests.ScalarAttributeType.String, "sk", global::DynaSharp.Requests.ScalarAttributeType.Number);

        // Act
        var elements = ResponseJson.KeyElements(keySchema);

        // Assert
        await Assert.That(string.Join(",", elements.Select(element => $"{element.AttributeName}:{element.KeyType}"))).IsEqualTo("pk:Hash,sk:Range");
    }

    private static JsonElement Render(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return JsonElement.Parse(buffer.WrittenSpan);
    }
}
