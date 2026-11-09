using System.Text;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Testing.Consumer;

namespace DynaSharp.UnitTests.Testing;

public sealed class RecordedRequestTests
{
    private const string PutBody = """{"TableName":"Subscribers","Item":{"pk":{"S":"ada"},"_type":{"S":"SUBSCRIBER"},"email":{"S":"ada@example.com"},"plan":{"S":"pro"}}}""";
    private const string GetBody = """{"TableName":"Subscribers","Key":{"pk":{"S":"ada"}}}""";

    [Test]
    public async Task BodyJson_Body_GivesTheParsedRoot()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var json = request.BodyJson;

        // Assert
        await Assert.That(json.GetProperty("TableName").GetString()).IsEqualTo("Subscribers");
    }

    [Test]
    public async Task BodyJson_ThirtyTwoLevelsOfDocument_IsReadWithTheLimitOfTheClient()
    {
        // Arrange (DynamoDB nests 32 levels, each two levels of JSON, which is beyond the default limit of a parser)
        var nested = new StringBuilder("""{"S":"deep"}""");
        for (var level = 0; level < 32; level++)
        {
            nested.Insert(0, """{"M":{"a":""").Append("}}");
        }

        var request = await RecordAsync("""{"Item":{"a":""" + nested + "}}");

        // Act
        var json = request.BodyJson;

        // Assert
        await Assert.That(json.GetProperty("Item").ValueKind).IsEqualTo(JsonValueKind.Object);
    }

    [Test]
    public async Task BodyJson_NotJson_Throws()
    {
        // Arrange
        var request = await RecordAsync("not json");

        // Act
        var act = () => request.BodyJson;

        // Assert
        await Assert.That(act).ThrowsException().And.IsAssignableTo<JsonException>();
    }

    [Test]
    public async Task ReadItem_NoType_ReturnsTheAttributesAsTheyWereSent()
    {
        // Arrange
        var request = await RecordAsync(PutBody);

        // Act
        var item = request.ReadItem();

        // Assert
        await Assert.That(item).IsEqualTo(new Item { ["pk"] = "ada", ["_type"] = "SUBSCRIBER", ["email"] = "ada@example.com", ["plan"] = "pro" });
    }

    [Test]
    public async Task ReadItem_Entity_ReadsItThroughItsMapping()
    {
        // Arrange
        var request = await RecordAsync(PutBody);

        // Act
        var subscriber = request.ReadItem<Subscriber>();

        // Assert
        await Assert.That(subscriber).IsEqualTo(new Subscriber("ada", "ada@example.com", "pro"));
    }

    [Test]
    public async Task ReadItem_ItemOfAnotherEntity_ThrowsAsARead()
    {
        // Arrange
        var request = await RecordAsync(PutBody);

        // Act
        var act = () => request.ReadItem<EmailClaim>();

        // Assert
        await Assert.That(act).Throws<DynamoDbMappingException>();
    }

    [Test]
    public async Task ReadItem_BodyWithoutTheMember_ThrowsNamingOperationAndMember()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var act = () => request.ReadItem();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage("The GetItem request has no member Item.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadItem_RequestWithoutOperation_StillNamesTheMember()
    {
        // Arrange
        var request = await RecordAsync(GetBody, target: null);

        // Act
        var act = () => request.ReadItem();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessage("The recorded request has no member Item.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadItem_Member_ReadsThatMember()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Subscribers","ExclusiveStartKey":{"pk":{"S":"ada"}}}""");

        // Act
        var key = request.ReadItem("ExclusiveStartKey");

        // Assert
        await Assert.That(key).IsEqualTo(new Item { ["pk"] = "ada" });
    }

    [Test]
    public async Task ReadItem_NullMember_Throws()
    {
        // Arrange
        var request = await RecordAsync(PutBody);

        // Act
        var act = () => request.ReadItem(null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>().WithParameterName("member");
    }

    [Test]
    public async Task ReadKey_NoType_ReturnsTheKeyAttributes()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var key = request.ReadKey();

        // Assert
        await Assert.That(key).IsEqualTo(StubResponse.KeyOf(new SubscriberKey("ada")));
    }

    [Test]
    public async Task ReadKey_TypeOfTheKeyAttributes_ReadsThroughIt()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var key = request.ReadKey<Item>();

        // Assert
        await Assert.That(key.GetString("pk")).IsEqualTo("ada");
    }

    [Test]
    public async Task ReadKey_EntityWithMoreRequiredAttributes_ThrowsAsARead()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var act = () => request.ReadKey<Subscriber>();

        // Assert
        await Assert.That(act).Throws<DynamoDbMappingException>();
    }

    [Test]
    public async Task TableName_RequestOnATable_IsTheTable()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var table = request.TableName;

        // Assert
        await Assert.That(table).IsEqualTo("Subscribers");
    }

    [Test]
    public async Task TableName_Batch_IsNull()
    {
        // Arrange (a batch names its tables in RequestItems)
        var request = await RecordAsync("""{"RequestItems":{"Subscribers":[{"DeleteRequest":{"Key":{"pk":{"S":"ada"}}}}]}}""", "DynamoDB_20120810.BatchWriteItem");

        // Act
        var table = request.TableName;

        // Assert
        await Assert.That(table).IsNull();
    }

    [Test]
    public async Task IndexName_QueryOfAnIndex_IsTheIndex()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Orders","IndexName":"ByCustomer","KeyConditionExpression":"#n0 = :v0"}""", "DynamoDB_20120810.Query");

        // Act
        var index = request.IndexName;

        // Assert
        await Assert.That(index).IsEqualTo("ByCustomer");
    }

    [Test]
    public async Task IndexName_ReadOfTheTable_IsNull()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var index = request.IndexName;

        // Assert
        await Assert.That(index).IsNull();
    }

    [Test]
    public async Task ConsistentRead_StronglyConsistentRead_IsTrue()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Subscribers","Key":{"pk":{"S":"ada"}},"ConsistentRead":true}""");

        // Act
        var consistent = request.ConsistentRead;

        // Assert
        await Assert.That(consistent).IsTrue();
    }

    [Test]
    public async Task ConsistentRead_FalseSent_IsFalse()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Subscribers","Key":{"pk":{"S":"ada"}},"ConsistentRead":false}""");

        // Act
        var consistent = request.ConsistentRead;

        // Assert
        await Assert.That(consistent).IsFalse();
    }

    [Test]
    public async Task ConsistentRead_NotSent_IsNull()
    {
        // Arrange
        var request = await RecordAsync(GetBody);

        // Act
        var consistent = request.ConsistentRead;

        // Assert
        await Assert.That(consistent).IsNull();
    }

    [Test]
    public async Task ConsistentRead_NotABoolean_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Subscribers","Key":{"pk":{"S":"ada"}},"ConsistentRead":"yes"}""");

        // Act
        var act = () => request.ConsistentRead;

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ScanIndexForward_Descending_IsFalse()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Orders","KeyConditionExpression":"#n0 = :v0","ScanIndexForward":false}""", "DynamoDB_20120810.Query");

        // Act
        var forward = request.ScanIndexForward;

        // Assert
        await Assert.That(forward).IsFalse();
    }

    [Test]
    public async Task ScanIndexForward_NotSent_IsNull()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Orders","KeyConditionExpression":"#n0 = :v0"}""", "DynamoDB_20120810.Query");

        // Act
        var forward = request.ScanIndexForward;

        // Assert
        await Assert.That(forward).IsNull();
    }

    [Test]
    public async Task Limit_Set_IsTheLimit()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Orders","Limit":25}""", "DynamoDB_20120810.Scan");

        // Act
        var limit = request.Limit;

        // Assert
        await Assert.That(limit).IsEqualTo(25);
    }

    [Test]
    public async Task Limit_NotSent_IsNull()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Orders"}""", "DynamoDB_20120810.Scan");

        // Act
        var limit = request.Limit;

        // Assert
        await Assert.That(limit).IsNull();
    }

    [Test]
    public async Task TableName_NoBody_ThrowsJsonException()
    {
        // Arrange
        var request = await RecordAsync(string.Empty);

        // Act
        var act = () => request.TableName;

        // Assert
        await Assert.That(act).ThrowsException().And.IsAssignableTo<JsonException>();
    }

    // What a test of a repository asks of the query it sent, without walking BodyJson: the table, the index, and how the page was read.
    [Test]
    public async Task Members_QueryOfAnIndexThroughAClient_ReadWhatTheClientSent()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Items(Array.Empty<Item>()));
        using var client = StubClient.Create(stub);
        await client.QueryAsync(new QueryRequest
        {
            TableName = "Orders",
            IndexName = "ByCustomer",
            KeyConditionExpression = "customer = :c",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":c"] = "alice" },
            ConsistentRead = true,
            ScanIndexForward = false,
            Limit = 10,
        });

        // Act
        var sent = stub.Requests.Single();

        // Assert
        await Assert.That((sent.TableName, sent.IndexName, sent.ConsistentRead, sent.ScanIndexForward, sent.Limit)).IsEqualTo(("Orders", "ByCustomer", true, false, 10));
    }

    private static async Task<RecordedRequest> RecordAsync(string body, string? target = "DynamoDB_20120810.GetItem")
    {
        using var stub = new StubHandler { DefaultResponse = new StubResponse() };
        using var response = await HandlerRequest.PostAsync(stub, body, target);
        return stub.Requests.Single();
    }
}
