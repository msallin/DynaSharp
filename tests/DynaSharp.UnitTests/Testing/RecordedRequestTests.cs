using System.Text;
using System.Text.Json;
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
        await Assert.That(act).Throws<ArgumentNullException>();
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

    private static async Task<RecordedRequest> RecordAsync(string body, string? target = "DynamoDB_20120810.GetItem")
    {
        using var stub = new StubHandler { DefaultResponse = new StubResponse() };
        using var response = await HandlerRequest.PostAsync(stub, body, target);
        return stub.Requests.Single();
    }
}
