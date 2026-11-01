using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class GetItemOperationTests
{
    private static readonly Dictionary<string, AttributeValue> Key = new() { ["pk"] = "user-1" };

    [Test]
    public async Task Name_Always_IsGetItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = GetItemOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("GetItem");
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key };

        // Act
        var tableName = GetItemOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
    }

    [Test]
    public async Task WriteRequest_TableAndKey_WritesOnlyThose()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task WriteRequest_CompositeKey_WritesBothKeyAttributes()
    {
        // Arrange
        var key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1", ["sk"] = AttributeValue.FromNumber(7) };
        var request = new GetItemRequest { TableName = "Orders", Key = key };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Orders","Key":{"pk":{"S":"user-1"},"sk":{"N":"7"}}}""");
    }

    [Test]
    public async Task WriteRequest_ConsistentRead_WritesTrue()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key, ConsistentRead = true };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"ConsistentRead":true}""");
    }

    [Test]
    public async Task WriteRequest_ProjectionAndNames_WritesBoth()
    {
        // Arrange
        var request = new GetItemRequest
        {
            TableName = "Users",
            Key = Key,
            ProjectionExpression = "#n, Address.City",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = "name" },
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"ProjectionExpression":"#n, Address.City","ExpressionAttributeNames":{"#n":"name"}}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionAttributeNames_OmitsThem()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key, ExpressionAttributeNames = new Dictionary<string, string>() };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task WriteRequest_ReturnConsumedCapacityTotal_WritesTotal()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key, ReturnConsumedCapacity = ReturnConsumedCapacity.Total };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task WriteRequest_ReturnConsumedCapacityIndexes_WritesIndexes()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key, ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"ReturnConsumedCapacity":"INDEXES"}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnConsumedCapacity_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key, ReturnConsumedCapacity = (ReturnConsumedCapacity)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnConsumedCapacity", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_Item_ReturnsTheItem()
    {
        // Arrange
        const string json = """{"Item":{"pk":{"S":"user-1"},"age":{"N":"42"}}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Item!["age"].AsNumber<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task ReadResponse_NoItem_ItemIsNull()
    {
        // Arrange (DynamoDB answers a missing item with an empty object)
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Item).IsNull();
    }

    [Test]
    public async Task ReadResponse_ConsumedCapacity_ReturnsIt()
    {
        // Arrange
        const string json = """{"ConsumedCapacity":{"CapacityUnits":0.5,"TableName":"Users","Future":{}},"Item":{"pk":{"S":"user-1"}}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.ConsumedCapacity).IsEqualTo(new ConsumedCapacity { TableName = "Users", CapacityUnits = 0.5 });
    }

    [Test]
    public async Task ReadResponse_DetailedConsumedCapacity_ReturnsAllTotals()
    {
        // Arrange (the shape DynamoDB returns for TOTAL, with a part this library does not model)
        const string json = """{"ConsumedCapacity":{"CapacityUnits":3.0,"ReadCapacityUnits":1.0,"WriteCapacityUnits":2.0,"Table":{"CapacityUnits":3.0},"TableName":"Users"}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.ConsumedCapacity).IsEqualTo(new ConsumedCapacity { TableName = "Users", CapacityUnits = 3, ReadCapacityUnits = 1, WriteCapacityUnits = 2, Table = new CapacityDetail { CapacityUnits = 3 } });
    }

    [Test]
    public async Task ReadResponse_UnknownProperties_AreSkipped()
    {
        // Arrange
        const string json = """{"Extra":{"a":[1,{"b":null}]},"Item":{"pk":{"S":"user-1"}},"Other":"x","More":[1,2],"Flag":true}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task ReadResponse_ConsumedCapacityWithIndexes_ReturnsTheCapacityOfTheTable()
    {
        // Arrange: shape from the ConsumedCapacity and Capacity structures of the DynamoDB service model.
        const string json = """{"ConsumedCapacity":{"TableName":"Users","CapacityUnits":3.0,"Table":{"ReadCapacityUnits":1.0,"WriteCapacityUnits":2.0,"CapacityUnits":3.0,"Future":1}}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.ConsumedCapacity!.Table).IsEqualTo(new CapacityDetail { CapacityUnits = 3, ReadCapacityUnits = 1, WriteCapacityUnits = 2 });
    }

    [Test]
    public async Task ReadResponse_ConsumedCapacityWithIndexes_ReturnsLocalAndGlobalIndexes()
    {
        // Arrange
        const string json = """{"ConsumedCapacity":{"LocalSecondaryIndexes":{"ByDate":{"CapacityUnits":0.5}},"GlobalSecondaryIndexes":{"ByEmail":{"CapacityUnits":1.5},"ByTeam":{"CapacityUnits":2.0}}}}""";

        // Act
        var capacity = Read(json).ConsumedCapacity!;

        // Assert
        await Assert.That((capacity.LocalSecondaryIndexes!["ByDate"].CapacityUnits, capacity.GlobalSecondaryIndexes!["ByEmail"].CapacityUnits, capacity.GlobalSecondaryIndexes["ByTeam"].CapacityUnits)).IsEqualTo((0.5, 1.5, 2.0));
    }

    [Test]
    public async Task ReadResponse_ConsumedCapacityWithNonNumberUnits_ThrowsInvalidOperationException()
    {
        // Arrange
        const string json = """{"ConsumedCapacity":{"CapacityUnits":"many"}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ReadResponse_NotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = "[]";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task SetActivityTags_ConsistentReadAndProjection_SetsBothTags()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key, ConsistentRead = true, ProjectionExpression = "#n" };
        using var activity = new System.Diagnostics.Activity("test");

        // Act
        GetItemOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo("aws.dynamodb.consistent_read=True,aws.dynamodb.projection=#n");
    }

    [Test]
    public async Task SetActivityTags_NoProjection_SetsOnlyConsistentRead()
    {
        // Arrange
        var request = new GetItemRequest { TableName = "Users", Key = Key };
        using var activity = new System.Diagnostics.Activity("test");

        // Act
        GetItemOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo("aws.dynamodb.consistent_read=False");
    }

    private static string Write(GetItemRequest request) => TestJson.Write(writer => GetItemOperation.WriteRequest(writer, request));

    private static GetItemResponse Read(string json) => TestJson.Read(json, static (ref reader) => GetItemOperation.ReadResponse(ref reader));
}
