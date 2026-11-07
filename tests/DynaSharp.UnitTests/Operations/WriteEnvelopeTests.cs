using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class WriteEnvelopeTests
{
    [Test]
    public async Task Read_EmptyObject_HasNothing()
    {
        // Arrange
        const string json = "{}";

        // Act
        var result = Read(json);

        // Assert
        await Assert.That(result).IsEqualTo(new WriteEnvelope<Item>(null, false, null, null));
    }

    [Test]
    public async Task Read_Attributes_ReturnsThem()
    {
        // Arrange
        const string json = """{"Attributes":{"pk":{"S":"a"},"views":{"N":"5"}}}""";

        // Act
        var result = Read(json);

        // Assert
        await Assert.That(result.Attributes!["views"].AsNumber<int>()).IsEqualTo(5);
    }

    [Test]
    public async Task Read_ConsumedCapacity_ReturnsIt()
    {
        // Arrange
        const string json = """{"ConsumedCapacity":{"TableName":"Users","CapacityUnits":1.0,"WriteCapacityUnits":1.0}}""";

        // Act
        var result = Read(json);

        // Assert
        await Assert.That(result.ConsumedCapacity).IsEqualTo(new ConsumedCapacity { TableName = "Users", CapacityUnits = 1, WriteCapacityUnits = 1 });
    }

    [Test]
    public async Task Read_ItemCollectionMetrics_ReturnsKeyAndRange()
    {
        // Arrange
        const string json = """{"ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0.5,1.5]}}""";

        // Act
        var result = Read(json);

        // Assert
        await Assert.That((result.ItemCollectionMetrics!.ItemCollectionKey!["pk"].AsString(), result.ItemCollectionMetrics.SizeEstimateRangeGB![0], result.ItemCollectionMetrics.SizeEstimateRangeGB[1]))
            .IsEqualTo(("a", 0.5, 1.5));
    }

    [Test]
    public async Task Read_PropertiesInAnyOrderWithUnknownOnes_ReturnsAllKnownMembers()
    {
        // Arrange
        const string json = """{"Unknown":{"a":[1]},"ItemCollectionMetrics":{"SizeEstimateRangeGB":[0.0,1.0],"Future":1},"ConsumedCapacity":{"CapacityUnits":2.0},"Attributes":{"pk":{"S":"a"}},"Trailing":[]}""";

        // Act
        var result = Read(json);

        // Assert
        await Assert.That((result.Attributes!.Count, result.ConsumedCapacity!.CapacityUnits, result.ItemCollectionMetrics!.SizeEstimateRangeGB!.Count)).IsEqualTo((1, 2.0, 2));
    }

    [Test]
    public async Task Read_NotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = "[]";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    private static WriteEnvelope<Item> Read(string json) => TestJson.Read(json, static (ref reader) => WriteEnvelope<Item>.Read(ref reader));
}
