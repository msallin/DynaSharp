using System.Net;
using System.Text.Json;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>
/// The first check of the error mapping against responses nobody wrote by hand. Each test sends the same failing call twice: through
/// the client, and as raw HTTP. The raw response is the oracle for the error code, the status, the message and the request id;
/// the client's exception must agree with it.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ErrorMappingTests(Backend backend)
{
    [Test]
    public async Task GetItem_MissingTable_ThrowsResourceNotFoundMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var json = $$$$$"""{"TableName":"{{{{{Tables.Missing}}}}}","Key":{"pk":{"S":"a"}}}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("GetItem", json);
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.GetItemAsync(
            new GetItemRequest { TableName = Tables.Missing, Key = Items.Key("a") }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.StatusCode)).IsEqualTo(("ResourceNotFoundException", HttpStatusCode.BadRequest));
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task PutItem_EmptyKeyValue_ThrowsValidationMatchingTheWire()
    {
        // Arrange: a key attribute may not be an empty string
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":""}}}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("PutItem", json);
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.PutItemAsync(
            new PutItemRequest { TableName = table, Item = Items.Key(string.Empty) }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.StatusCode)).IsEqualTo(("ValidationException", HttpStatusCode.BadRequest));
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task GetItem_KeyDoesNotMatchTheSchema_ThrowsValidationMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TableName":"{{{{{table}}}}}","Key":{"not-the-key":{"S":"a"}}}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("GetItem", json);
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.GetItemAsync(
            new GetItemRequest { TableName = table, Key = new Dictionary<string, AttributeValue> { ["not-the-key"] = "a" } }));

        // Assert
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task Query_MalformedExpression_ThrowsValidationMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TableName":"{{{{{table}}}}}","KeyConditionExpression":"pk ==","ExpressionAttributeValues":{":pk":{"S":"a"}}}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("Query", json);
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            KeyConditionExpression = "pk ==",
            ExpressionAttributeValues = Items.Values((":pk", "a")),
        }));

        // Assert
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task PutItem_ConditionFails_ThrowsConditionalCheckFailedMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "stored") });
        var json = $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"a"}},"ConditionExpression":"attribute_not_exists(pk)","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("PutItem", json);
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = Items.Key("a"),
            ConditionExpression = "attribute_not_exists(pk)",
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
        }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.StatusCode)).IsEqualTo(("ConditionalCheckFailedException", HttpStatusCode.BadRequest));
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task PutItem_ConditionFails_ItemOfTheExceptionIsTheItemOnTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var stored = Items.Item("a", "stored");
        stored["n"] = AttributeValue.FromNumber("1.5");
        stored["tags"] = AttributeValue.FromStringSet(["x", "y"]);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = stored });
        var json = $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"a"}},"ConditionExpression":"attribute_not_exists(pk)","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("PutItem", json);
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = Items.Key("a"),
            ConditionExpression = "attribute_not_exists(pk)",
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
        }));

        // Assert
        using var wire = JsonDocument.Parse(raw.Body);
        await Assert.That(wire.RootElement.GetProperty("Item").GetProperty("n").GetProperty("N").GetString()).IsEqualTo(failure.Item!["n"].AsNumber());
        await Assert.That(failure.Item).IsEquivalentTo(stored);
    }

    [Test]
    public async Task GetItem_ErrorResponse_CarriesTheRequestIdOfTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.GetItemAsync(
            new GetItemRequest { TableName = Tables.Missing, Key = Items.Key("a") }));

        // Assert
        await Assert.That(string.IsNullOrEmpty(failure.RequestId)).IsFalse();
    }

    [Test]
    public async Task TransactWriteItems_ConditionFails_ThrowsTransactionCanceledWithTheReasonsOfTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var stored = Items.Item("taken", "stored");
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = stored });
        var json = $$$$$"""
            {"TransactItems":[
              {"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"fresh"}}}},
              {"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"taken"}},"ConditionExpression":"attribute_not_exists(pk)","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}},
              {"ConditionCheck":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"absent"}},"ConditionExpression":"attribute_exists(pk)"}}]}
            """;

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", json);
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Key("fresh") } },
                new TransactWriteItem
                {
                    Put = new TransactPut
                    {
                        TableName = table,
                        Item = Items.Key("taken"),
                        ConditionExpression = "attribute_not_exists(pk)",
                        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
                    },
                },
                new TransactWriteItem { ConditionCheck = new TransactConditionCheck { TableName = table, Key = Items.Key("absent"), ConditionExpression = "attribute_exists(pk)" } },
            ],
        }));

        // Assert
        await AssertAgreesWithTheWireAsync(failure, raw);
        using var wire = JsonDocument.Parse(raw.Body);
        var wireReasons = wire.RootElement.GetProperty("CancellationReasons").EnumerateArray().ToList();
        await Assert.That(failure.CancellationReasons.Count).IsEqualTo(wireReasons.Count);
        for (var i = 0; i < wireReasons.Count; i++)
        {
            var reason = failure.CancellationReasons[i];
            await Assert.That(reason.Code).IsEqualTo(wireReasons[i].GetProperty("Code").GetString());
            await Assert.That(reason.Message).IsEqualTo(wireReasons[i].TryGetProperty("Message", out var message) ? message.GetString() : null);
            await Assert.That(reason.Item is not null).IsEqualTo(wireReasons[i].TryGetProperty("Item", out _));
        }
    }

    [Test]
    public async Task TransactWriteItems_TokenReusedWithOtherActions_ThrowsIdempotentParameterMismatchMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var token = Guid.NewGuid().ToString("N");
        await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            ClientRequestToken = token,
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Key("first") } }],
        });
        var json = $$$$$"""{"ClientRequestToken":"{{{{{token}}}}}","TransactItems":[{"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"second"}}}}]}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", json);
        var failure = await Items.FailureOfAsync<IdempotentParameterMismatchException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            ClientRequestToken = token,
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Key("second") } }],
        }));

        // Assert
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task CreateTable_NameInUse_ThrowsResourceInUseMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TableName":"{{{{{table}}}}}","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("CreateTable", json);
        var failure = await Items.FailureOfAsync<ResourceInUseException>(() => sandbox.Client.CreateTableAsync(Tables.Request(table, TableSpec.PartitionOnly)));

        // Assert
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task BatchWriteItem_TwoOperationsOnOneKey_ThrowsValidationMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"RequestItems":{"{{{{{table}}}}}":[{"PutRequest":{"Item":{"pk":{"S":"a"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"a"}}}}]}}""";

        // Act
        var raw = await sandbox.Emulator.Raw.SendAsync("BatchWriteItem", json);
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.BatchWriteItemAsync(new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
            {
                [table] = [new WriteRequest { PutRequest = new PutRequest { Item = Items.Key("a") } }, new WriteRequest { DeleteRequest = new DeleteRequest { Key = Items.Key("a") } }],
            },
        }));

        // Assert
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    [Test]
    public async Task GetRecords_InvalidIterator_ThrowsValidationMatchingTheWire()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        const string json = """{"ShardIterator":"not-an-iterator"}""";

        // Act
        var raw = await sandbox.Emulator.RawStreams.SendAsync("GetRecords", json);
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Streams.GetRecordsAsync(new GetRecordsRequest { ShardIterator = "not-an-iterator" }));

        // Assert
        await AssertAgreesWithTheWireAsync(failure, raw);
    }

    // The wire is right by definition. The client must report its code and its status; the message may be reworded by the
    // client but not lose the service's text; the request id of one call differs from the next, so only its presence is compared.
    private static async Task AssertAgreesWithTheWireAsync(DynamoDbException failure, RawResponse raw)
    {
        using var wire = JsonDocument.Parse(raw.Body);
        var root = wire.RootElement;
        var wireMessage = (root.TryGetProperty("message", out var lower) ? lower : root.GetProperty("Message")).GetString();

        await Assert.That(failure.ErrorCode).IsEqualTo(raw.ErrorCode);
        await Assert.That(failure.StatusCode).IsEqualTo(raw.StatusCode);

        // An empty wire message becomes a description of the error.
        await Assert.That(failure.Message).IsEqualTo(string.IsNullOrEmpty(wireMessage) ? $"DynamoDB returned the error {failure.ErrorCode} (HTTP 400) without a message." : wireMessage);
        await Assert.That(string.IsNullOrEmpty(failure.RequestId)).IsEqualTo(string.IsNullOrEmpty(raw.RequestId));
    }
}
