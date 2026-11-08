using System.Net;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><c>TrySendAsync</c>, which takes a failed condition as an answer. What the three writes share is tested here, on the put; the update and the delete test what is theirs.</summary>
public sealed partial class PutCommandTests
{
    private static readonly Customer Alice = new("alice", "Alice", 3, "alice@example.com");

    [Test]
    public async Task TrySendAsync_ConditionFailed_ConditionMetIsFalse()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Put(Alice).IfNotExists().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.HasItem)).IsEqualTo((false, false));
    }

    [Test]
    public async Task TrySendAsync_ConditionFailedWithTheItem_ReadsItAsTheEntity()
    {
        // Arrange
        var stored = Alice with { FullName = "Alice Stored" };
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed(stored));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Put(Alice).IfNotExists().ReturnOldOnFailure().TrySendAsync();

        // Assert
        await Assert.That((result.HasItem, result.Item)).IsEqualTo((true, stored));
    }

    [Test]
    public async Task TrySendAsync_ConditionFailedOnATableOfItems_ReadsTheItem()
    {
        // Arrange
        var stored = new Item { ["pk"] = "a", ["n"] = 2 };
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed(stored));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Put(new Item { ["pk"] = "a", ["n"] = 3 }).IfNotExists().ReturnOldOnFailure().TrySendAsync();

        // Assert
        await Assert.That(result.Item).IsEqualTo(stored);
    }

    [Test]
    public async Task TrySendAsync_ConditionFailed_HasNoVersionCapacityOrMetrics()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var ticket = new Ticket { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 };

        // Act
        var result = await client.Table<Ticket>("Shop").Put(ticket).ReturnCapacity().ReturnMetrics().TrySendAsync();

        // Assert
        await Assert.That((result.Version, result.ConsumedCapacity, result.ItemCollectionMetrics)).IsEqualTo((null, null, null));
    }

    [Test]
    public async Task TrySendAsync_ConditionHeld_ConditionMetIsTrue()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var ticket = new Ticket { Id = "t1", OpenedAt = new DateOnly(2026, 10, 31), Revision = 3 };

        // Act
        var result = await client.Table<Ticket>("Shop").Put(ticket).TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.Version)).IsEqualTo((true, 4L));
    }

    [Test]
    public async Task TrySendAsync_Always_SendsWhatSendAsyncSends()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        await customers.Put(Alice).IfNotExists().ReturnOldOnFailure().SendAsync();
        await customers.Put(Alice).IfNotExists().ReturnOldOnFailure().TrySendAsync();

        // Assert
        await Assert.That(handler.Requests[1].BodyText).IsEqualTo(handler.Requests[0].BodyText);
    }

    [Test]
    public async Task TrySendAsync_OtherError_Throws()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.DynamoDbError("ValidationException", "bad"));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.Table<Customer>("Shop").Put(Alice).IfNotExists().TrySendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task TrySendAsync_ErrorWithoutAType_ThrowsAnUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"message":"no type"}""", HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(async () => await client.Table<Customer>("Shop").Put(Alice).TrySendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task TrySendAsync_ErrorBodyThatIsNotJson_ThrowsAnUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("<html>bad gateway</html>", HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(async () => await client.Table<Customer>("Shop").Put(Alice).TrySendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task TrySendAsync_ErrorBodyThatIsNoObject_ThrowsAnUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("[]", HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(async () => await client.Table<Customer>("Shop").Put(Alice).TrySendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task TrySendAsync_ItemThatDoesNotFit_ThrowsMappingException()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json(
            """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","Item":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"ORDER"}},"message":"The conditional request failed"}""",
            HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Customer>("Shop").Put(Alice).ReturnOldOnFailure().TrySendAsync());

        // Assert
        await Assert.That(failure.EntityType).IsEqualTo(nameof(Customer));
    }

    [Test]
    public async Task TrySendAsync_ConditionFailed_IsNotRetried()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, maxAttempts: 3);

        // Act
        await client.Table<Customer>("Shop").Put(Alice).IfNotExists().TrySendAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TrySendAsync_ConditionFailedOnTheRetry_IsAnAnswer()
    {
        // Arrange (the first attempt fails in the service, the second meets a condition)
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? StubResponse.DynamoDbError("InternalServerError", "oops", HttpStatusCode.InternalServerError) : StubResponse.ConditionalCheckFailed(Alice));
        using var client = TestClient.Create(handler, maxAttempts: 2, random: FixedRandom.Zero);

        // Act
        var result = await client.Table<Customer>("Shop").Put(Alice).IfNotExists().ReturnOldOnFailure().TrySendAsync();

        // Assert
        await Assert.That((result.ConditionMet, result.Item, handler.Requests.Count)).IsEqualTo((false, Alice, 2));
    }

    [Test]
    public async Task TrySendAsync_InsideRetryOnConflict_RunsOnce()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        var runs = 0;

        // Act
        await customers.RetryOnConflictAsync(async token =>
        {
            runs++;
            return await customers.Put(Alice).IfNotExists().TrySendAsync(token);
        });

        // Assert
        await Assert.That(runs).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_AfterATrySendOfTheSamePool_ThrowsForAFailedCondition()
    {
        // Arrange (the state of the first command goes back to the pool, and the next one must not inherit what TrySendAsync set)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        await customers.Put(Alice).IfNotExists().TrySendAsync();

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await customers.Put(Alice).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ConditionalCheckFailedException");
    }
}
