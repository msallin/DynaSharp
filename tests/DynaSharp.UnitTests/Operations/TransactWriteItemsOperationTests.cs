using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Telemetry.ActivityCapture;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Operations;

public sealed class TransactWriteItemsOperationTests
{
    private const string Token = "3d2f7c8e-5a41-4b6e-9a0f-2c1d8e7b6a54";

    // The counterparts of the parameters in generate_operation_vectors.py, by vector name. The expected JSON is what botocore wrote
    // for the parameters there.
    private static readonly Dictionary<string, TransactWriteItemsRequest> BotocoreRequests = new()
    {
        ["PutOnly"] = Request(),
        ["NoToken"] = Request() with { ClientRequestToken = null },
        ["EveryActionKindWithEveryOption"] = new()
        {
            TransactItems = [new TransactWriteItem { ConditionCheck = StockCheck() }, new TransactWriteItem { Put = OrderPut() }, new TransactWriteItem { Update = CustomerUpdate() }, new TransactWriteItem { Delete = CouponDelete() }],
            ReturnConsumedCapacity = ReturnConsumedCapacity.Indexes,
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
            ClientRequestToken = Token,
        },
        ["EveryActionKindMinimal"] = new()
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    ConditionCheck = new TransactConditionCheck
                    {
                        TableName = "Customers",
                        Key = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1" },
                        ConditionExpression = "attribute_exists(pk)",
                    },
                },
                new TransactWriteItem { Put = new TransactPut { TableName = "Orders", Item = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" } } },
                new TransactWriteItem
                {
                    Update = new TransactUpdate
                    {
                        TableName = "Customers",
                        Key = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1" },
                        UpdateExpression = "ADD orders :one",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":one"] = AttributeValue.FromNumber("1") },
                    },
                },
                new TransactWriteItem { Delete = new TransactDelete { TableName = "Coupons", Key = new Dictionary<string, AttributeValue> { ["code"] = "WELCOME10" } } },
            ],
            ClientRequestToken = Token,
        },
        ["ReturnValuesOnConditionCheckFailureAllOldOnEachKind"] = new()
        {
            TransactItems =
            [
                new TransactWriteItem { ConditionCheck = StockCheck() },
                new TransactWriteItem { Put = new TransactPut { TableName = "Orders", Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld } },
                new TransactWriteItem
                {
                    Update = new TransactUpdate
                    {
                        TableName = "Customers",
                        Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" },
                        UpdateExpression = "SET a = :a",
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":a"] = AttributeValue.FromNumber("1") },
                        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
                    },
                },
                new TransactWriteItem { Delete = new TransactDelete { TableName = "Coupons", Key = new Dictionary<string, AttributeValue> { ["code"] = "a" }, ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld } },
            ],
            ClientRequestToken = Token,
        },
        ["AllAttributeValueTypes"] = new()
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new TransactPut { TableName = "Users", Item = OperationVectors.AllValueTypes().Append(KeyValuePair.Create("pk", (AttributeValue)"user-1")).ToDictionary() } },
                new TransactWriteItem
                {
                    Update = new TransactUpdate
                    {
                        TableName = "Users",
                        Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-2" },
                        UpdateExpression = "SET a = :s",
                        ExpressionAttributeValues = OperationVectors.AllValueTypes(),
                    },
                },
            ],
            ClientRequestToken = Token,
        },
        ["HundredActions"] = new()
        {
            TransactItems = [.. Enumerable.Range(0, 100).Select(index => new TransactWriteItem { Put = new TransactPut { TableName = "Users", Item = OperationVectors.NumberedKey(index) } })],
            ClientRequestToken = Token,
        },
        ["SpecialCharacters"] = new()
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new TransactPut
                    {
                        TableName = OperationVectors.SpecialText,
                        Item = new Dictionary<string, AttributeValue> { [OperationVectors.SpecialText] = OperationVectors.SpecialText },
                        ConditionExpression = "#n = :v",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = OperationVectors.SpecialText },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = OperationVectors.SpecialText },
                    },
                },
            ],
            ClientRequestToken = Token,
        },
        ["ReturnConsumedCapacityTotalAndTableArn"] = new()
        {
            TransactItems = [new TransactWriteItem { Delete = new TransactDelete { TableName = OperationVectors.TableArn, Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } } }],
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
            ClientRequestToken = "a",
        },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("TransactWriteItems");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("TransactWriteItems");

    [Test]
    public async Task Name_Always_IsTransactWriteItems()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = TransactWriteItemsOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("TransactWriteItems");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("TransactWriteItems", "PutOnly");

        // Act
        var target = WireProtocol.TargetPrefix + TransactWriteItemsOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_OneTableOnEveryKindOfAction_ReturnsItsName()
    {
        // Arrange
        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { ConditionCheck = StockCheck() with { TableName = "Users" } },
                new TransactWriteItem { Put = OrderPut() with { TableName = "Users" } },
                new TransactWriteItem { Update = CustomerUpdate() with { TableName = "Users" } },
                new TransactWriteItem { Delete = CouponDelete() with { TableName = "Users" } },
            ],
        };

        // Act
        var tableName = TransactWriteItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
    }

    [Test]
    public async Task GetTableName_SeveralTables_ReturnsNull()
    {
        // Arrange
        var request = BotocoreRequests["EveryActionKindWithEveryOption"];

        // Act
        var tableName = TransactWriteItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task GetTableName_NoAction_ReturnsNull()
    {
        // Arrange
        var request = new TransactWriteItemsRequest { TransactItems = [] };

        // Act
        var tableName = TransactWriteItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task GetTableName_ActionWithNoMember_IsIgnored()
    {
        // Arrange
        var request = new TransactWriteItemsRequest { TransactItems = [new TransactWriteItem(), new TransactWriteItem { Put = OrderPut() }] };

        // Act
        var tableName = TransactWriteItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Orders");
    }

    [Test]
    public async Task GetTableName_ActionWithMembersForTwoTables_ReturnsNull()
    {
        // Arrange (invalid for DynamoDB, but the tables are still the ones the request names)
        var request = new TransactWriteItemsRequest { TransactItems = [new TransactWriteItem { Put = OrderPut(), Delete = CouponDelete() }] };

        // Act
        var tableName = TransactWriteItemsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task BotocoreRequests_Always_CoverEveryVector()
    {
        // Arrange (the vectors come from the generator, the requests from this class)

        // Act
        var missing = RequestVectorNames().Except(BotocoreRequests.Keys).Concat(BotocoreRequests.Keys.Except(RequestVectorNames()));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(RequestVectorNames))]
    public async Task WriteRequest_BotocoreVector_WritesTheJsonThatBotocoreWrites(string name)
    {
        // Arrange (botocore leaves the token to its client; the library makes one, which the vector cannot have)
        var vector = OperationVectors.Request("TransactWriteItems", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        var actual = request.ClientRequestToken is null ? WithoutToken(json) : json;
        await Assert.That(JsonComparer.Differences(vector.Body, actual)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_RequiredMembersOnly_WritesTheActionAndTheToken()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"user-1"}}}}],"ClientRequestToken":"3d2f7c8e-5a41-4b6e-9a0f-2c1d8e7b6a54"}""");
    }

    [Test]
    public async Task WriteRequest_EverythingSet_WritesInRequestOrder()
    {
        // Arrange
        var request = BotocoreRequests["EveryActionKindWithEveryOption"];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TransactItems":[{"ConditionCheck":{"TableName":"Stock","Key":{"sku":{"S":"SKU-1"},"warehouse":{"N":"7"}},"ConditionExpression":"quantity >= :needed","ExpressionAttributeValues":{":needed":{"N":"2"}},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}},{"Put":{"TableName":"Orders","Item":{"pk":{"S":"CUSTOMER#1"},"sk":{"S":"ORDER#1"},"total":{"N":"12.5"}},"ConditionExpression":"attribute_not_exists(pk)","ExpressionAttributeNames":{"#t":"total"},"ExpressionAttributeValues":{":min":{"N":"0"}},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}},{"Update":{"TableName":"Customers","Key":{"pk":{"S":"CUSTOMER#1"}},"UpdateExpression":"SET orders = orders + :one, #s = :active","ConditionExpression":"attribute_exists(pk) AND version = :version","ExpressionAttributeNames":{"#s":"status"},"ExpressionAttributeValues":{":one":{"N":"1"},":active":{"S":"ACTIVE"},":version":{"N":"4"}},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}},{"Delete":{"TableName":"Coupons","Key":{"code":{"S":"WELCOME10"}},"ConditionExpression":"#u = :unused","ExpressionAttributeNames":{"#u":"used"},"ExpressionAttributeValues":{":unused":{"BOOL":false}},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}}],"ReturnConsumedCapacity":"INDEXES","ReturnItemCollectionMetrics":"SIZE","ClientRequestToken":"3d2f7c8e-5a41-4b6e-9a0f-2c1d8e7b6a54"}""");
    }

    [Test]
    public async Task WriteRequest_NoToken_GeneratesAGuidInTheFormatOfTheLongestAllowedToken()
    {
        // Arrange
        var request = Request() with { ClientRequestToken = null };

        // Act
        var token = TokenOf(Write(request));

        // Assert
        await Assert.That((token.Length, Guid.TryParseExact(token, "D", out _))).IsEqualTo((36, true));
    }

    [Test]
    public async Task WriteRequest_NoToken_GeneratesANewTokenForEveryRequestSerialized()
    {
        // Arrange
        var request = Request() with { ClientRequestToken = null };

        // Act
        var first = TokenOf(Write(request));
        var second = TokenOf(Write(request));

        // Assert
        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    [Arguments("a")]
    [Arguments("my-token-0001")]
    [Arguments("0123456789012345678901234567890123456")]
    [Arguments("")]
    [Arguments("Zürich \"quoted\"")]
    public async Task WriteRequest_Token_IsWrittenAsGivenAndDynamoDbValidatesIt(string token)
    {
        // Arrange
        var request = Request() with { ClientRequestToken = token };

        // Act
        var written = TokenOf(Write(request));

        // Assert
        await Assert.That(written).IsEqualTo(token);
    }

    [Test]
    public async Task WriteRequest_ActionWithNoMember_WritesAnEmptyObjectAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = Request() with { TransactItems = [new TransactWriteItem()] };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(WithoutToken(json)).IsEqualTo("""{"TransactItems":[{}]}""");
    }

    [Test]
    public async Task WriteRequest_ActionWithSeveralMembers_WritesAllAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = Request() with
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new TransactPut { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" } },
                    Delete = new TransactDelete { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "b" } },
                },
            ],
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(WithoutToken(json)).IsEqualTo("""{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}},"Delete":{"TableName":"Users","Key":{"pk":{"S":"b"}}}}]}""");
    }

    [Test]
    public async Task WriteRequest_EmptyExpressionMaps_OmitsThem()
    {
        // Arrange
        var request = Request() with
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new TransactPut
                    {
                        TableName = "Users",
                        Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" },
                        ExpressionAttributeNames = new Dictionary<string, string>(),
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>(),
                    },
                },
            ],
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(WithoutToken(json)).IsEqualTo("""{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}}]}""");
    }

    [Test]
    public async Task WriteRequest_NoActions_WritesAnEmptyArrayAndLeavesTheRejectionToDynamoDb()
    {
        // Arrange
        var request = Request() with { TransactItems = [] };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(WithoutToken(json)).IsEqualTo("""{"TransactItems":[]}""");
    }

    [Test]
    public async Task WriteRequest_UnknownReturnValuesOnConditionCheckFailure_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { TransactItems = [new TransactWriteItem { Put = OrderPut() with { ReturnValuesOnConditionCheckFailure = (ReturnValuesOnConditionCheckFailure)99 } }] };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnValuesOnConditionCheckFailure", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_UnknownReturnConsumedCapacity_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { ReturnConsumedCapacity = (ReturnConsumedCapacity)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnConsumedCapacity", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_UnknownReturnItemCollectionMetrics_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known ReturnItemCollectionMetrics", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_EveryMember_MapsEachToItsProperty()
    {
        // Arrange (every value differs, so a swapped property cannot go unnoticed)
        const string json = """
            {"ConsumedCapacity":[{"TableName":"Orders","CapacityUnits":3.5},{"TableName":"Customers","CapacityUnits":1.5}],
             "ItemCollectionMetrics":{"Orders":[{"ItemCollectionKey":{"pk":{"S":"c"}},"SizeEstimateRangeGB":[0.5,1.5]}]}}
            """;

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(JsonComparer.Differences(json, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_EmptyObject_HasEmptyCollections()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.ConsumedCapacity.Count, response.ItemCollectionMetrics.Count)).IsEqualTo((0, 0));
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("TransactWriteItems", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        var expected = ResponseJson.WithDefaults(vector.Parsed, ("ConsumedCapacity", new JsonArray()), ("ItemCollectionMetrics", new JsonObject()));
        await Assert.That(JsonComparer.Differences(expected, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("""{"ConsumedCapacity":{}}""")]
    [Arguments("""{"ItemCollectionMetrics":[]}""")]
    [Arguments("""{"ItemCollectionMetrics":{"Orders":{}}}""")]
    [Arguments("""{"ItemCollectionMetrics":{"Orders":[{"SizeEstimateRangeGB":{}}]}}""")]
    [Arguments("""{"ConsumedCapacity":[{"TableName":"Orders"}""")]
    [Arguments("[]")]
    public async Task ReadResponse_MalformedBody_ThrowsJsonException(string json)
    {
        // Arrange (see json)

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task SetActivityTags_OneTable_SetsTableNamesToThatTable()
    {
        // Arrange
        var request = Request();
        using var activity = new Activity("test");

        // Act
        TransactWriteItemsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Users");
    }

    [Test]
    public async Task SetActivityTags_EveryKindOfActionOnSeveralTables_SetsDistinctTableNamesSortedOrdinally()
    {
        // Arrange
        var request = BotocoreRequests["EveryActionKindWithEveryOption"] with
        {
            TransactItems =
            [
                new TransactWriteItem { Put = OrderPut() with { TableName = "users" } },
                new TransactWriteItem { Update = CustomerUpdate() with { TableName = "Customers" } },
                new TransactWriteItem { Delete = CouponDelete() with { TableName = "Coupons" } },
                new TransactWriteItem { ConditionCheck = StockCheck() with { TableName = "Stock" } },
                new TransactWriteItem { Put = OrderPut() with { TableName = "users" } },
            ],
        };
        using var activity = new Activity("test");

        // Act
        TransactWriteItemsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(Tags(activity)).IsEqualTo("aws.dynamodb.table_names=Coupons,Customers,Stock,users");
    }

    [Test]
    public async Task SetActivityTags_Request_SetsNoTokenKeysOrExpressions()
    {
        // Arrange
        var request = BotocoreRequests["EveryActionKindWithEveryOption"];
        using var activity = new Activity("test");

        // Act
        TransactWriteItemsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.TagObjects.Select(tag => tag.Key).ToArray()).IsEquivalentTo(["aws.dynamodb.table_names"]);
    }

    private static TransactConditionCheck StockCheck() => new()
    {
        TableName = "Stock",
        Key = new Dictionary<string, AttributeValue> { ["sku"] = "SKU-1", ["warehouse"] = AttributeValue.FromNumber("7") },
        ConditionExpression = "quantity >= :needed",
        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":needed"] = AttributeValue.FromNumber("2") },
        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
    };

    private static TransactPut OrderPut() => new()
    {
        TableName = "Orders",
        Item = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1", ["total"] = AttributeValue.FromNumber("12.5") },
        ConditionExpression = "attribute_not_exists(pk)",
        ExpressionAttributeNames = new Dictionary<string, string> { ["#t"] = "total" },
        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":min"] = AttributeValue.FromNumber("0") },
        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
    };

    private static TransactUpdate CustomerUpdate() => new()
    {
        TableName = "Customers",
        Key = new Dictionary<string, AttributeValue> { ["pk"] = "CUSTOMER#1" },
        UpdateExpression = "SET orders = orders + :one, #s = :active",
        ConditionExpression = "attribute_exists(pk) AND version = :version",
        ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
        {
            [":one"] = AttributeValue.FromNumber("1"),
            [":active"] = "ACTIVE",
            [":version"] = AttributeValue.FromNumber("4"),
        },
        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
    };

    private static TransactDelete CouponDelete() => new()
    {
        TableName = "Coupons",
        Key = new Dictionary<string, AttributeValue> { ["code"] = "WELCOME10" },
        ConditionExpression = "#u = :unused",
        ExpressionAttributeNames = new Dictionary<string, string> { ["#u"] = "used" },
        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":unused"] = AttributeValue.FromBool(false) },
        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
    };

    private static TransactWriteItemsRequest Request() => new()
    {
        TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } } }],
        ClientRequestToken = Token,
    };

    private static string WithoutToken(string json)
    {
        var request = JsonNode.Parse(json)!.AsObject();
        request.Remove("ClientRequestToken");
        return request.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Write(TransactWriteItemsRequest request) => TestJson.Write(writer => TransactWriteItemsOperation.WriteRequest(writer, request));

    private static TransactWriteItemsResponse Read(string json) => TestJson.Read(json, static (ref reader) => TransactWriteItemsOperation.ReadResponse(ref reader));
}
