using System.Net;
using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

// Bodies after the API reference, TransactWriteItems, and the Developer Guide, "Managing complex workflows with DynamoDB transactions": all or nothing, one reason per action
// when cancelled, two writes per item, and a client request token remembered for 10 minutes.
public sealed class TransactWriteItemsTests
{
    private const string PutA = """{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"},"v":{"S":"first"}}}}""";

    [Test]
    public async Task Handle_PutDeleteAndUpdate_AppliesThemAll()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"gone"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "TransactWriteItems",
            """
            {"TransactItems":[
              {"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}},
              {"Delete":{"TableName":"Users","Key":{"pk":{"S":"gone"}}}},
              {"Update":{"TableName":"Orders","Key":{"pk":{"S":"o"},"sk":{"N":"1"}},"UpdateExpression":"ADD n :one","ExpressionAttributeValues":{":one":{"N":"1"}}}}]}
            """);

        // Assert
        await Assert.That((FakeCalls.SameJson("{}", body), fake.Items("Users").Single()["pk"].AsString(), fake.Items("Orders").Single()["n"].AsNumber())).IsEqualTo((true, "a", "1"));
    }

    [Test]
    public async Task Handle_OneConditionFails_CancelsWithOneReasonPerActionAndWritesNothing()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"b"},"v":{"S":"old"}}}""");

        // Act
        var (status, body) = FakeCalls.Send(
            fake,
            "TransactWriteItems",
            $$$$$"""
            {"TransactItems":[{{{{{PutA}}}}},
              {"ConditionCheck":{"TableName":"Users","Key":{"pk":{"S":"b"}},"ConditionExpression":"attribute_not_exists(pk)","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}}]}
            """);

        // Assert
        await Assert.That((status, FakeCalls.SameJson(
            """
            {"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException",
             "message":"Transaction cancelled, please refer cancellation reasons for specific reasons [None, ConditionalCheckFailed]",
             "CancellationReasons":[{"Code":"None"},{"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{"pk":{"S":"b"},"v":{"S":"old"}}}]}
            """,
            body), fake.Items("Users").Count)).IsEqualTo((HttpStatusCode.BadRequest, true, 1));
    }

    [Test]
    public async Task Handle_ConditionFailsWithoutAllOld_LeavesTheItemOutOfTheReason()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (_, body) = FakeCalls.Send(fake, "TransactWriteItems", """{"TransactItems":[{"Delete":{"TableName":"Users","Key":{"pk":{"S":"a"}},"ConditionExpression":"attribute_exists(pk)"}}]}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""[{"Code":"ConditionalCheckFailed","Message":"The conditional request failed"}]""", body.GetProperty("CancellationReasons"))).IsTrue();
    }

    [Test]
    [Arguments("""{"Put":{"TableName":"Users","Item":{"pk":{"N":"1"}}}}""")]
    [Arguments("""{"Delete":{"TableName":"Users","Key":{"other":{"S":"a"}}}}""")]
    public async Task Handle_KeyThatDoesNotMatchTheSchema_CancelsWithValidationErrorForThatAction(string action)
    {
        // Arrange: DynamoDB Local cancels with a reason here, see Vectors/writes.json; the first action is fine and gets None
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (_, body) = FakeCalls.Send(fake, "TransactWriteItems", """{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}},""" + action + "]}");

        // Assert
        var codes = body.GetProperty("CancellationReasons").EnumerateArray().Select(static reason => reason.GetProperty("Code").GetString());
        await Assert.That(string.Join(",", codes)).IsEqualTo("None,ValidationError");
    }

    [Test]
    public async Task Handle_WriteTheTableRejects_CancelsWithValidationErrorAndTakesTheEarlierWritesBack()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"b"},"v":{"S":"old"}}}""");

        // Act
        var (_, body) = FakeCalls.Send(
            fake,
            "TransactWriteItems",
            $$$$$"""
            {"TransactItems":[{{{{{PutA}}}}},{"Delete":{"TableName":"Users","Key":{"pk":{"S":"b"}}}},
              {"Put":{"TableName":"Users","Item":{"pk":{"S":"c"},"v":{"S":"{{{{{FakeWrites.Text(410_000)}}}}}"}}}}]}
            """);

        // Assert
        await Assert.That((FakeCalls.SameJson(
            """[{"Code":"None"},{"Code":"None"},{"Code":"ValidationError","Message":"Item size has exceeded the maximum allowed size"}]""",
            body.GetProperty("CancellationReasons")), fake.Items("Users").Single()["v"].AsString())).IsEqualTo((true, "old"));
    }

    [Test]
    public async Task Handle_TotalCapacity_ReportsTwoWritesPerItemPerTable()
    {
        // Arrange: a condition check costs as a write of the item it reads
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Orders","Item":{"pk":{"S":"o"},"sk":{"N":"1"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "TransactWriteItems",
            $$$$$"""
            {"TransactItems":[{{{{{PutA}}}}},{"Put":{"TableName":"Users","Item":{"pk":{"S":"b"}}}},
              {"ConditionCheck":{"TableName":"Orders","Key":{"pk":{"S":"o"},"sk":{"N":"1"}},"ConditionExpression":"attribute_exists(pk)"}}],
             "ReturnConsumedCapacity":"TOTAL"}
            """);

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """{"ConsumedCapacity":[{"TableName":"Users","CapacityUnits":4},{"TableName":"Orders","CapacityUnits":2}]}""",
            body)).IsTrue();
    }

    [Test]
    public async Task Handle_IndexesAndSize_ReportsTheIndexesAndTheItemCollections()
    {
        // Arrange
        var fake = FakeCalls.With(FakeWrites.Forum);

        // Act
        var body = FakeCalls.Ok(
            fake,
            "TransactWriteItems",
            """
            {"TransactItems":[{"Put":{"TableName":"Forum","Item":{"pk":{"S":"a"},"sk":{"S":"1"},"author":{"S":"ann"}}}},
                              {"ConditionCheck":{"TableName":"Forum","Key":{"pk":{"S":"b"},"sk":{"S":"1"}},"ConditionExpression":"attribute_not_exists(pk)"}}],
             "ReturnConsumedCapacity":"INDEXES","ReturnItemCollectionMetrics":"SIZE"}
            """);

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"ConsumedCapacity":[{"TableName":"Forum","CapacityUnits":6,"Table":{"CapacityUnits":4},
                                  "GlobalSecondaryIndexes":{"ByAuthor":{"CapacityUnits":2}}}],
             "ItemCollectionMetrics":{"Forum":[{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0,1]}]}}
            """,
            body)).IsTrue();
    }

    [Test]
    public async Task Handle_SameTokenAndRequestAgain_ChangesNothing()
    {
        // Arrange: the first call writes v=first, a put overwrites it, the replay must not write again
        var fake = FakeCalls.With(FakeCalls.Users);
        var request = $$$$$"""{"ClientRequestToken":"t-1","TransactItems":[{{{{{PutA}}}}}]}""";
        FakeCalls.Ok(fake, "TransactWriteItems", request);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"a"},"v":{"S":"second"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "TransactWriteItems", $$$$$"""{"TransactItems":[{{{{{PutA}}}}}],"ClientRequestToken":"t-1"}""");

        // Assert
        await Assert.That((FakeCalls.SameJson("{}", body), fake.Items("Users").Single()["v"].AsString())).IsEqualTo((true, "second"));
    }

    [Test]
    public async Task Handle_ReplayWithTotal_ReportsTheReadUnitsOfReadingTheItems()
    {
        // Arrange: the API reference says a repeated call returns the read capacity units of reading the item, not the write units of the first call
        var fake = FakeCalls.With(FakeCalls.Users);
        var request = $$$"""{"ClientRequestToken":"t-1","ReturnConsumedCapacity":"TOTAL","TransactItems":[{{{PutA}}}]}""";
        FakeCalls.Ok(fake, "TransactWriteItems", request);

        // Act
        var body = FakeCalls.Ok(fake, "TransactWriteItems", request);

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":[{"TableName":"Users","CapacityUnits":1}]}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ReplayWithSize_ReportsTheItemCollections()
    {
        // Arrange
        var fake = FakeCalls.With(FakeWrites.Forum);
        var request = """
            {"ClientRequestToken":"t-1","ReturnItemCollectionMetrics":"SIZE","TransactItems":[{"Put":{"TableName":"Forum","Item":{"pk":{"S":"a"},"sk":{"S":"1"}}}},
              {"ConditionCheck":{"TableName":"Forum","Key":{"pk":{"S":"b"},"sk":{"S":"1"}},"ConditionExpression":"attribute_not_exists(pk)"}}]}
            """;
        FakeCalls.Ok(fake, "TransactWriteItems", request);

        // Act
        var body = FakeCalls.Ok(fake, "TransactWriteItems", request);

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ItemCollectionMetrics":{"Forum":[{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0,1]}]}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_SameTokenWithAnotherRequest_AnswersIdempotentParameterMismatch()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "TransactWriteItems", $$$$$"""{"ClientRequestToken":"t-1","TransactItems":[{{{{{PutA}}}}}]}""");

        // Act
        var answer = FakeCalls.Error(fake, "TransactWriteItems", """{"ClientRequestToken":"t-1","TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"b"}}}}]}""");

        // Assert
        await Assert.That((answer.Status, answer.Code, fake.Items("Users").Count)).IsEqualTo((HttpStatusCode.BadRequest, "IdempotentParameterMismatchException", 1));
    }

    [Test]
    [Arguments(9, 59, 1)]
    [Arguments(10, 0, 2)]
    public async Task Handle_SameTokenLater_ReplaysOnlyWithinTenMinutes(int minutes, int seconds, int items)
    {
        // Arrange: the second call with the token puts b; within the window it is a mismatch, after it a new transaction
        var clock = new ManualTimeProvider();
        var fake = new FakeDynamoDb(clock);
        FakeCalls.Ok(fake, "CreateTable", FakeCalls.Users);
        FakeCalls.Ok(fake, "TransactWriteItems", $$$$$"""{"ClientRequestToken":"t-1","TransactItems":[{{{{{PutA}}}}}]}""");
        clock.Advance(new TimeSpan(0, minutes, seconds));

        // Act
        fake.Answer("TransactWriteItems", """{"ClientRequestToken":"t-1","TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"b"}}}}]}""");

        // Assert
        await Assert.That(fake.Items("Users").Count).IsEqualTo(items);
    }

    [Test]
    public async Task Handle_TokenOfACancelledTransaction_IsNotRemembered()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Send(fake, "TransactWriteItems", """{"ClientRequestToken":"t-1","TransactItems":[{"Delete":{"TableName":"Users","Key":{"pk":{"S":"a"}},"ConditionExpression":"attribute_exists(pk)"}}]}""");

        // Act
        FakeCalls.Ok(fake, "TransactWriteItems", $$$$$"""{"ClientRequestToken":"t-1","TransactItems":[{{{{{PutA}}}}}]}""");

        // Assert
        await Assert.That(fake.Items("Users").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Handle_TokenAfterReset_IsNew()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "TransactWriteItems", $$$$$"""{"ClientRequestToken":"t-1","TransactItems":[{{{{{PutA}}}}}]}""");
        fake.Reset();
        FakeCalls.Ok(fake, "CreateTable", FakeCalls.Users);

        // Act
        FakeCalls.Ok(fake, "TransactWriteItems", """{"ClientRequestToken":"t-1","TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"b"}}}}]}""");

        // Assert
        await Assert.That(fake.Items("Users").Single()["pk"].AsString()).IsEqualTo("b");
    }

    [Test]
    [Arguments("""{"TransactItems":[]}""", "1 validation error detected: Value at 'transactItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("""{"TransactItems":[{}]}""", "TransactItems can only contain one of Check, Put, Update or Delete")]
    [Arguments("""{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}},"Delete":{"TableName":"Users","Key":{"pk":{"S":"b"}}}}]}""", "TransactItems can only contain one of Check, Put, Update or Delete")]
    [Arguments("""{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}},{"Delete":{"TableName":"Users","Key":{"pk":{"S":"a"}}}}]}""", "Transaction request cannot include multiple operations on one item")]
    [Arguments("""{"TransactItems":[{"ConditionCheck":{"TableName":"Users","Key":{"pk":{"S":"a"}}}}]}""", "1 validation error detected: Value null at 'conditionExpression' failed to satisfy constraint: Member must not be null")]
    [Arguments("""{"TransactItems":[{"Update":{"TableName":"Users","Key":{"pk":{"S":"a"}}}}]}""", "1 validation error detected: Value null at 'updateExpression' failed to satisfy constraint: Member must not be null")]
    [Arguments("""{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}}],"ClientRequestToken":""}""", "1 validation error detected: Value '' at 'clientRequestToken' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments(
        """{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}}],"ClientRequestToken":"0123456789012345678901234567890123456"}""",
        "1 validation error detected: Value '0123456789012345678901234567890123456' at 'clientRequestToken' failed to satisfy constraint: Member must have length less than or equal to 36")]
    [Arguments("""{"TransactItems":[{"Update":{"TableName":"Users","Key":{"pk":{"S":"a"}},"UpdateExpression":"SET pk = :v","ExpressionAttributeValues":{":v":{"S":"b"}}}}]}""", "One or more parameter values were invalid: Cannot update attribute pk. This attribute is part of the key")]
    public async Task Handle_RequestDynamoDbRejects_AnswersValidationAndWritesNothing(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var answer = FakeCalls.Error(fake, "TransactWriteItems", request);

        // Assert
        await Assert.That((answer, fake.Items("Users").Count)).IsEqualTo(((HttpStatusCode.BadRequest, "ValidationException", message), 0));
    }

    [Test]
    public async Task Handle_UpdateTheItemCannotTake_CancelsWithValidationError()
    {
        // Arrange: ADD of a number to a string fails only on the item
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"a"},"n":{"S":"text"}}}""");

        // Act
        var (_, body) = FakeCalls.Send(
            fake,
            "TransactWriteItems",
            """{"TransactItems":[{"Update":{"TableName":"Users","Key":{"pk":{"S":"a"}},"UpdateExpression":"ADD n :one","ExpressionAttributeValues":{":one":{"N":"1"}}}}]}""");

        // Assert
        await Assert.That(body.GetProperty("CancellationReasons")[0].GetProperty("Code").GetString()).IsEqualTo("ValidationError");
    }

    [Test]
    public async Task Handle_101Actions_AnswersTheLengthConstraint()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        var puts = string.Join(",", Enumerable.Range(0, 101).Select(i => $$$$$"""{"Put":{"TableName":"Users","Item":{"pk":{"S":"u#{{{{{i}}}}}"}}}}"""));

        // Act
        var answer = FakeCalls.Error(fake, "TransactWriteItems", $$$$$"""{"TransactItems":[{{{{{puts}}}}}]}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo("1 validation error detected: Value at 'transactItems' failed to satisfy constraint: Member must have length less than or equal to 100");
    }

    [Test]
    [Arguments("""{"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}},"ReturnValues":"ALL_OLD"}}]}""", "the member ReturnValues in Put of TransactWriteItems")]
    [Arguments("""{"TransactItems":[{"Delete":{"TableName":"Users","Key":{"pk":{"S":"a"}},"Expected":{}}}]}""", "the member Expected in Delete of TransactWriteItems")]
    [Arguments("""{"TransactItems":[{"Get":{}}]}""", "the member Get in TransactItems of TransactWriteItems")]
    public async Task Handle_MemberTransactWriteItemsDoesNotHave_AnswersUnsupported(string request, string named)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "TransactWriteItems", request);

        // Assert
        await Assert.That((code, message.Contains(named, StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }

    [Test]
    public async Task Handle_ThroughAClient_ThrowsTransactionCanceledWithTheReasons()
    {
        // Arrange: the client sends a token of its own, so this also runs the token path end to end
        var fake = FakeCalls.With(FakeCalls.Users);
        using var client = fake.CreateClient();
        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Delete = new TransactDelete
                    {
                        TableName = "Users",
                        Key = new Item { ["pk"] = AttributeValue.FromString("a") },
                        ConditionExpression = "attribute_exists(pk)",
                    },
                },
            ],
        };

        // Act
        var failure = await Assert.ThrowsAsync<TransactionCanceledException>(async () => await client.TransactWriteItemsAsync(request));

        // Assert
        await Assert.That(failure!.CancellationReasons!.Single().Code).IsEqualTo("ConditionalCheckFailed");
    }
}
