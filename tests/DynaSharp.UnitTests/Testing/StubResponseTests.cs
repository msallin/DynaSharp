using System.Net;
using System.Text.Json;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent;
using DynaSharp.UnitTests.Mapping;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using DynaSharp.UnitTests.Testing.Consumer;

namespace DynaSharp.UnitTests.Testing;

public sealed class StubResponseTests
{
    public static IEnumerable<string> RealGetItems() => RealAwsResponses.Names("GetItem");

    [Test]
    public async Task Constructor_Default_StatusCodeIsOk()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new StubResponse();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Constructor_Default_BodyIsAnEmptyJsonObject()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new StubResponse();

        // Assert
        await Assert.That(response.Body).IsEqualTo("{}");
    }

    [Test]
    public async Task Json_BodyOnly_UsesStatusOk()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Json("""{"a":1}""");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Json_Body_KeepsTheBodyAsGiven()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Json("""{"a":1}""");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"a":1}""");
    }

    [Test]
    public async Task DynamoDbError_CodeAndMessage_WritesTheDynamoDbErrorShape()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"__type":"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException","message":"Requested resource not found"}""");
    }

    [Test]
    public async Task DynamoDbError_Default_StatusIsBadRequest()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.DynamoDbError("ValidationException", "invalid");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DynamoDbError_StatusGiven_UsesIt()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.DynamoDbError("InternalServerError", "boom", HttpStatusCode.InternalServerError);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
    }

    [Test]
    public async Task DynamoDbError_MessageWithQuotesAndBackslashes_WritesValidJson()
    {
        // Arrange
        const string message = "Value \"a\\b\" at 'tableName' failed";

        // Act
        var response = StubResponse.DynamoDbError("ValidationException", message);

        // Assert
        await Assert.That(JsonElement.Parse(response.Body).GetProperty("message").GetString()).IsEqualTo(message);
    }

    [Test]
    public async Task DynamoDbError_RequestId_SetsTheRequestIdHeader()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.DynamoDbError("ValidationException", "invalid", requestId: "REQ-1");

        // Assert
        await Assert.That(response.Headers!["x-amzn-RequestId"]).IsEqualTo("REQ-1");
    }

    [Test]
    public async Task DynamoDbError_NoRequestId_HasNoHeaders()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.DynamoDbError("ValidationException", "invalid");

        // Assert
        await Assert.That(response.Headers).IsNull();
    }

    [Test]
    public async Task Item_Item_WritesItsAttributesUnderItem()
    {
        // Arrange
        var item = new Item { ["pk"] = "user-1", ["age"] = 30 };

        // Act
        var response = StubResponse.Item(item);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Item":{"pk":{"S":"user-1"},"age":{"N":"30"}}}""");
    }

    [Test]
    public async Task Item_Entity_WritesWhatTheEntityIsStoredAs()
    {
        // Arrange
        var subscriber = new Subscriber("ada", "ada@example.com", "pro");

        // Act
        var response = StubResponse.Item(subscriber);

        // Assert
        await Assert.That(JsonComparer.Differences("""{"Item":{"pk":{"S":"ada"},"email":{"S":"ada@example.com"},"plan":{"S":"pro"},"_type":{"S":"SUBSCRIBER"}}}""", response.Body)).IsEmpty();
    }

    [Test]
    public async Task Item_NestedDocumentsAndNumbers_WritesThemAsTheCodecDoes()
    {
        // Arrange (a hand-written entity with its own key prefix: the helper must go through the entity, not around it)
        var customer = new Customer("alice", "Älice \"A\"", 3, "alice@example.com");

        // Act
        var response = StubResponse.Item(customer);

        // Assert
        await Assert.That(JsonComparer.Differences(
            """{"Item":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Älice \"A\""},"orderCount":{"N":"3"},"email":{"S":"alice@example.com"}}}""",
            response.Body)).IsEmpty();
    }

    [Test]
    public async Task Item_StructEntity_WritesItToo()
    {
        // Arrange (a struct has no null, so it has no miss either)
        var reading = new Reading { Sensor = 7 };

        // Act
        var response = StubResponse.Item(reading);

        // Assert
        await Assert.That(JsonElement.Parse(response.Body).GetProperty("Item").GetProperty("Sensor").GetProperty("N").GetString()).IsEqualTo("7");
    }

    [Test]
    public async Task Item_Null_IsTheAnswerOfAMiss()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Item<Subscriber>(null);

        // Assert
        await Assert.That(response.Body).IsEqualTo("{}");
    }

    [Test]
    [MethodDataSource(nameof(RealGetItems))]
    public async Task Item_ItemOfARealResponse_WritesThatResponse(string name)
    {
        // Arrange (recorded from real DynamoDB; the order of the members of a read item carries no meaning)
        var wire = RealAwsResponses.Get(name).Wire;
        var item = ReadItemOf(wire);

        // Act
        var response = StubResponse.Item(item);

        // Assert
        await Assert.That(JsonComparer.Differences(wire, response.Body)).IsEmpty();
    }

    [Test]
    public async Task Items_ContainsHitOfARealResponse_WritesThatResponse()
    {
        // Arrange
        var item = new Item
        {
            ["pk"] = "esm-contains",
            ["tags"] = AttributeValue.FromStringSet([string.Empty, "a"]),
            ["sk"] = "with-empty",
        };

        // Act
        var response = StubResponse.Items([item]);

        // Assert
        await Assert.That(response.Body).IsEqualTo(RealAwsResponses.Get("esm_query_contains_hit").Wire);
    }

    [Test]
    public async Task Items_FilterDroppedAllOfARealResponse_WritesThatResponse()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Items(Array.Empty<Item>(), scannedCount: 1);

        // Assert
        await Assert.That(response.Body).IsEqualTo(RealAwsResponses.Get("esm_query_contains_miss").Wire);
    }

    [Test]
    public async Task Items_ItemKey_WritesLastEvaluatedKeySoThatTheReadGoesOn()
    {
        // Arrange
        Item[] items = [new Item { ["pk"] = "a" }, new Item { ["pk"] = "b" }];

        // Act
        var response = StubResponse.Items(items, new Item { ["pk"] = "b" }, 5);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Items":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}],"Count":2,"ScannedCount":5,"LastEvaluatedKey":{"pk":{"S":"b"}}}""");
    }

    [Test]
    public async Task Items_GeneratedKey_MatchesTheEmptyPageOfBotocore()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Items(Array.Empty<Item>(), new OrderPosition("CUSTOMER#1", "ORDER#100"), 100);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseWire("Query", "EmptyPageWithLastEvaluatedKey"), response.Body)).IsEmpty();
    }

    [Test]
    public async Task Items_ScannedCountBelowTheItems_ThrowsNamingTheParameter()
    {
        // Arrange
        Item[] items = [new Item { ["pk"] = "a" }, new Item { ["pk"] = "b" }];

        // Act
        var act = () => StubResponse.Items(items, scannedCount: 1);

        // Assert
        var failure = await Assert.That(act).Throws<ArgumentOutOfRangeException>();
        await Assert.That(failure!.ParamName).IsEqualTo("scannedCount");
    }

    [Test]
    public async Task Items_NullItems_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Items<Item>(null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Items_NullItemKey_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Items(Array.Empty<Item>(), (Item)null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Items_NullGeneratedKey_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Items(Array.Empty<Item>(), (DynaSharp.Mapping.IKey)null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Attributes_Entity_WritesWhatTheEntityIsStoredAs()
    {
        // Arrange
        var subscriber = new Subscriber("ada", "ada@example.com");

        // Act
        var response = StubResponse.Attributes(subscriber);

        // Assert
        await Assert.That(JsonComparer.Differences("""{"Attributes":{"pk":{"S":"ada"},"email":{"S":"ada@example.com"},"plan":{"S":"free"},"_type":{"S":"SUBSCRIBER"}}}""", response.Body)).IsEmpty();
    }

    [Test]
    public async Task Attributes_FewAttributes_WritesThePartialAnswerOfBotocore()
    {
        // Arrange
        var updated = new Item { ["Views"] = 6, ["Tags"] = AttributeValue.FromStringSet(["Update"]) };

        // Act
        var response = StubResponse.Attributes(updated);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Attributes":{"Views":{"N":"6"},"Tags":{"SS":["Update"]}}}""");
    }

    [Test]
    public async Task Attributes_Null_MatchesTheDeleteOfNothingOfBotocore()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Attributes<Item>(null);

        // Assert
        await Assert.That(response.Body).IsEqualTo(ResponseWire("DeleteItem", "NothingDeleted"));
    }

    [Test]
    public async Task ConditionalCheckFailed_NoItem_WritesTheErrorAsDynamoDbSendsIt()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.ConditionalCheckFailed();

        // Assert
        await Assert.That((response.StatusCode, response.Body)).IsEqualTo((
            HttpStatusCode.BadRequest,
            """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","message":"The conditional request failed"}"""));
    }

    [Test]
    public async Task ConditionalCheckFailed_Item_WritesTheItemBeforeTheMessage()
    {
        // Arrange
        var item = new Item { ["pk"] = "user-1", ["version"] = 3 };

        // Act
        var response = StubResponse.ConditionalCheckFailed(item);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","Item":{"pk":{"S":"user-1"},"version":{"N":"3"}},"message":"The conditional request failed"}""");
    }

    [Test]
    public async Task ConditionalCheckFailed_NullItem_IsTheErrorWithoutOne()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.ConditionalCheckFailed<Subscriber>(null);

        // Assert
        await Assert.That(response.Body).IsEqualTo(StubResponse.ConditionalCheckFailed().Body);
    }

    [Test]
    public async Task TransactionCanceled_Failures_WritesTheRealResponse()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TransactionCanceled(TransactionFailure.ConditionalCheckFailed, TransactionFailure.None);

        // Assert
        await Assert.That((response.StatusCode, response.Body)).IsEqualTo(((HttpStatusCode)OperationVectors.Error("ConditionFailedOnTheFirstAction").Status, OperationVectors.Error("ConditionFailedOnTheFirstAction").Wire));
    }

    [Test]
    public async Task TransactionCanceled_ReasonWithAnItem_WritesTheItemInTheReason()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.TransactionCanceled(
            StubResponse.Reason(TransactionFailure.None),
            StubResponse.ConditionalCheckFailedReason(new Item { ["pk"] = "user-1", ["version"] = 3 }));

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"None"},{"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{"pk":{"S":"user-1"},"version":{"N":"3"}}}],"Message":"Transaction cancelled, please refer cancellation reasons for specific reasons [None, ConditionalCheckFailed]"}""");
    }

    [Test]
    public async Task TransactionCanceled_ReasonOfYourOwn_WritesWhatItHolds()
    {
        // Arrange (a code from the future, and a reason without anything)

        // Act
        var response = StubResponse.TransactionCanceled(new CancellationReason { Code = "ProvisionedThroughputExceeded", Message = "slow down" }, new CancellationReason());

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"ProvisionedThroughputExceeded","Message":"slow down"},{}],"Message":"Transaction cancelled, please refer cancellation reasons for specific reasons [ProvisionedThroughputExceeded, ]"}""");
    }

    [Test]
    [Arguments(TransactionFailure.None)]
    [Arguments(TransactionFailure.ConditionalCheckFailed)]
    [Arguments(TransactionFailure.ItemCollectionSizeLimitExceeded)]
    [Arguments(TransactionFailure.TransactionConflict)]
    [Arguments(TransactionFailure.Throttled)]
    [Arguments(TransactionFailure.ValidationError)]
    public async Task TransactionCanceled_Failure_IsReadBackAsThatFailure(TransactionFailure failure)
    {
        // Arrange (a transaction of one action, whose marker the exception looks up)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TransactionCanceled(failure));
        using var client = TestClient.Create(handler);
        TransactMarker marker = default;

        // Act
        var canceled = await TestClient.CaptureAsync<TransactionCanceledException>(async () =>
            await client.TransactWrite().Put(client.Table("Shop", "pk"), new Item { ["pk"] = "a" }, out marker).SendAsync());

        // Assert
        await Assert.That(canceled.ReasonFor(marker)).IsEqualTo(failure);
    }

    [Test]
    [Arguments(TransactionFailure.ConditionalCheckFailed, "ConditionalCheckFailed", "The conditional request failed")]
    [Arguments(TransactionFailure.ItemCollectionSizeLimitExceeded, "ItemCollectionSizeLimitExceeded", "Collection size exceeded.")]
    [Arguments(TransactionFailure.TransactionConflict, "TransactionConflict", "Transaction is ongoing for the item")]
    [Arguments(TransactionFailure.Throttled, "ThrottlingError", "Throughput exceeds the current capacity of your table or index. DynamoDB is automatically scaling your table or index so please try again shortly.")]
    [Arguments(TransactionFailure.ValidationError, "ValidationError", "One or more parameter values were invalid.")]
    public async Task Reason_Failure_UsesTheCodeAndMessageOfTheService(TransactionFailure failure, string code, string message)
    {
        // Arrange (nothing to arrange)

        // Act
        var reason = StubResponse.Reason(failure);

        // Assert
        await Assert.That((reason.Code, reason.Message)).IsEqualTo((code, message));
    }

    [Test]
    public async Task Reason_None_HasNoMessage()
    {
        // Arrange (nothing to arrange)

        // Act
        var reason = StubResponse.Reason(TransactionFailure.None);

        // Assert
        await Assert.That((reason.Code, reason.Message)).IsEqualTo(("None", (string?)null));
    }

    [Test]
    [Arguments(TransactionFailure.Unknown)]
    [Arguments((TransactionFailure)99)]
    public async Task Reason_ValueWithoutACode_Throws(TransactionFailure failure)
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Reason(failure);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ConditionalCheckFailedReason_Null_HasNoItem()
    {
        // Arrange (nothing to arrange)

        // Act
        var reason = StubResponse.ConditionalCheckFailedReason<Subscriber>(null);

        // Assert
        await Assert.That(reason).IsEqualTo(StubResponse.Reason(TransactionFailure.ConditionalCheckFailed));
    }

    [Test]
    public async Task Unprocessed_Writes_MatchesTheResponseOfTheApiReference()
    {
        // Arrange (the reference also shows ConsumedCapacity, which a request must ask for)
        var item = new Item { ["Name"] = "Amazon ElastiCache", ["Category"] = "Amazon Web Services" };

        // Act
        var response = StubResponse.Unprocessed("Forum", StubResponse.Put(item));

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"UnprocessedItems":{"Forum":[{"PutRequest":{"Item":{"Name":{"S":"Amazon ElastiCache"},"Category":{"S":"Amazon Web Services"}}}}]}}""");
    }

    [Test]
    public async Task Unprocessed_PutAndDelete_WritesEachInItsForm()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Unprocessed(
            "Orders",
            StubResponse.Put(new Subscriber("ada", "ada@example.com")),
            StubResponse.Delete(new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#0" }),
            StubResponse.Delete(new OrderPosition("CUSTOMER#1", "ORDER#1")));

        // Assert
        await Assert.That(JsonComparer.Differences(
            """
            {"UnprocessedItems":{"Orders":[
                {"PutRequest":{"Item":{"pk":{"S":"ada"},"email":{"S":"ada@example.com"},"plan":{"S":"free"},"_type":{"S":"SUBSCRIBER"}}}},
                {"DeleteRequest":{"Key":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#0"}}}},
                {"DeleteRequest":{"Key":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#1"}}}}]}}
            """,
            response.Body)).IsEmpty();
    }

    [Test]
    public async Task Unprocessed_NoWrites_MatchesTheEmptyAnswerOfBotocore()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Unprocessed("Orders");

        // Assert
        await Assert.That(response.Body).IsEqualTo(ResponseWire("BatchWriteItem", "EmptyUnprocessedItems"));
    }

    [Test]
    public async Task Unprocessed_KeysOfARead_WritesTheFormOfTheRequestWithTheWayOfReading()
    {
        // Arrange (the Orders table of the vector NothingReadAllKeysUnprocessed, whose other table is one this helper does not write)
        var keys = StubResponse.Keys(new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#2" }, new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#3" }) with
        {
            ConsistentRead = true,
            ProjectionExpression = "#s, total",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
        };

        // Act
        var response = StubResponse.Unprocessed("Orders", keys);

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            """{"Responses":{},"UnprocessedKeys":{"Orders":{"Keys":[{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#2"}},{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#3"}}],"ConsistentRead":true,"ProjectionExpression":"#s, total","ExpressionAttributeNames":{"#s":"status"}}}}""");
    }

    [Test]
    public async Task Unprocessed_GeneratedKeysAndFoundItems_WritesBoth()
    {
        // Arrange
        Subscriber[] found = [new Subscriber("ada", "ada@example.com")];

        // Act
        var response = StubResponse.Unprocessed("Subscribers", StubResponse.Keys(new SubscriberKey("grace"), new SubscriberKey("linus")), found);

        // Assert
        await Assert.That(JsonComparer.Differences(
            """
            {"Responses":{"Subscribers":[{"pk":{"S":"ada"},"email":{"S":"ada@example.com"},"plan":{"S":"free"},"_type":{"S":"SUBSCRIBER"}}]},
             "UnprocessedKeys":{"Subscribers":{"Keys":[{"pk":{"S":"grace"}},{"pk":{"S":"linus"}}]}}}
            """,
            response.Body)).IsEmpty();
    }

    [Test]
    public async Task Unprocessed_NullArguments_Throw()
    {
        // Arrange
        var keys = StubResponse.Keys(new Item { ["pk"] = "a" });
        Func<StubResponse>[] calls =
        [
            () => StubResponse.Unprocessed(null!),
            () => StubResponse.Unprocessed(null!, keys),
            () => StubResponse.Unprocessed("Shop", (DynaSharp.Requests.KeysAndAttributes)null!),
            () => StubResponse.Unprocessed(null!, keys, Array.Empty<Item>()),
            () => StubResponse.Unprocessed("Shop", null!, Array.Empty<Item>()),
            () => StubResponse.Unprocessed<Item>("Shop", keys, null!),
        ];

        // Act
        var failures = calls.Count(static call =>
        {
            try
            {
                call();
                return false;
            }
            catch (ArgumentNullException)
            {
                return true;
            }
        });

        // Assert
        await Assert.That(failures).IsEqualTo(calls.Length);
    }

    [Test]
    public async Task BatchItems_Items_WritesThemUnderTheTableAndNothingUnprocessed()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.BatchItems("Forum", [new Item { ["Name"] = "Amazon RDS", ["Threads"] = 8 }]);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Responses":{"Forum":[{"Name":{"S":"Amazon RDS"},"Threads":{"N":"8"}}]},"UnprocessedKeys":{}}""");
    }

    [Test]
    public async Task BatchItems_NoItems_KeepsTheTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.BatchItems("Forum", Array.Empty<Item>());

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Responses":{"Forum":[]},"UnprocessedKeys":{}}""");
    }

    [Test]
    public async Task BatchItems_NullTable_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.BatchItems(null!, Array.Empty<Item>());

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task BatchItems_NullItems_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.BatchItems<Item>("Forum", null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Put_ItemWrittenBefore_KeepsItsAttributes()
    {
        // Arrange
        var item = new Item { ["pk"] = "a" };

        // Act
        var write = StubResponse.Put(item);
        item["pk"] = "changed";

        // Assert
        await Assert.That(write.PutRequest!.Item["pk"].AsString()).IsEqualTo("a");
    }

    [Test]
    public async Task Put_Null_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Put<Item>(null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Delete_NullItem_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Delete((Item)null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Delete_NullKey_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Delete((DynaSharp.Mapping.IKey)null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task KeyOf_GeneratedKey_HoldsTheAttributesItWrites()
    {
        // Arrange (nothing to arrange)

        // Act
        var key = StubResponse.KeyOf(new OrderPosition("CUSTOMER#1", "ORDER#1"));

        // Assert
        await Assert.That(key).IsEqualTo(new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" });
    }

    [Test]
    public async Task KeyOf_Entity_HoldsOnlyTheKey()
    {
        // Arrange (nothing to arrange)

        // Act
        var key = StubResponse.KeyOf(new Customer("alice", "Alice"));

        // Assert
        await Assert.That(key).IsEqualTo(new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE" });
    }

    [Test]
    public async Task KeyOf_NullKey_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.KeyOf(null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task KeyOf_NullEntity_Throws()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.KeyOf<Customer>(null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    private static string ResponseWire(string operation, string name) =>
        OperationVectors.Responses.Single(vector => vector.Operation == operation && vector.Name == name).Wire;

    // Reads the item of a response body without the code under test.
    private static Item ReadItemOf(string wire)
    {
        var reader = new DynaSharp.Serialization.DynamoDbReader(System.Text.Encoding.UTF8.GetBytes(JsonElement.Parse(wire).GetProperty("Item").GetRawText()));
        reader.ReadStartItem();
        return DynaSharp.Serialization.AttributeValueConverter.ReadAttributes(ref reader);
    }

    /// <summary>A key as the generator writes it, with a sort key.</summary>
    private readonly record struct OrderPosition(string Pk, string Sk) : DynaSharp.Mapping.IKey
    {
        public void WriteKey(DynaSharp.Serialization.DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString(Pk);
            writer.WriteAttributeName("sk"u8);
            writer.WriteString(Sk);
        }
    }
}
