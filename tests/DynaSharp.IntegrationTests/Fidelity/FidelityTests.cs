using System.Net;
using System.Text.Json.Nodes;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Fidelity;

/// <summary>
/// The questions about DynamoDB that <c>FakeDynamoDb</c>, DynamoDB Local and Dynoxide answered differently (<c>spec/tasks.md</c>, R20), asked of real DynamoDB on the raw wire.
/// Each test asserts what real DynamoDB answered in <c>real-aws.yml</c> run 42356511937; where that run left the rule open, the test accepts the answers seen and records which one
/// came. Either way the answer is written to the output of the test and, when <c>DYNASHARP_FIDELITY_REPORT</c> names a file, to that file as one JSON line.
/// </summary>
/// <remarks>
/// Real DynamoDB and the fake are asserted: the answer of DynamoDB is the spec of the fake. An emulator that answers otherwise records its answer and is skipped with it in the reason,
/// because what it does there is no fault of the code under test; <c>Differential/KnownDivergences</c> names the same quirks for the generated sequences.
/// </remarks>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class FidelityTests(Backend backend)
{
    /// <summary>The name of the environment variable that names the report file.</summary>
    public const string ReportVariableName = "DYNASHARP_FIDELITY_REPORT";

    private const string Canceled = "TransactionCanceledException";

    private const string KeyMismatch = "ValidationError: The provided key element does not match the schema";

    private const string Overflow = "ValidationError: Number overflow. Attempting to store a number with magnitude larger than supported range";

    private static readonly Lock ReportLock = new();

    // What each spelling is in plain digits, the form DynamoDB returns it in.
    private static readonly Dictionary<string, string> PlainDigits = new(StringComparer.Ordinal)
    {
        ["1.0"] = "1",
        ["01.50"] = "1.5",
        ["1E+2"] = "100",
        ["-0"] = "0",
        ["1E+125"] = "1" + new string('0', 125),
        ["1E-130"] = "0." + new string('0', 129) + "1",
    };

    // Question 1: ReturnConsumedCapacity is one of the parameters a token pins, as the documentation says of "a change in other parameters".
    [Test]
    public async Task TransactWriteItems_SameTokenOtherReturnConsumedCapacity_IsAParameterMismatch()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var token = Guid.NewGuid().ToString("N");
        string Request(string capacity) =>
            $$$$$"""{"ClientRequestToken":"{{{{{token}}}}}","ReturnConsumedCapacity":"{{{{{capacity}}}}}","TransactItems":[{"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"a"}}}}]}""";
        (await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", Request("NONE"))).EnsureSuccess();

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", Request("TOTAL"));
        var outcome = Record("1 token with another ReturnConsumedCapacity", Outcome(response), response, "IdempotentParameterMismatchException");

        // Assert
        await Assert.That(outcome).IsEqualTo("IdempotentParameterMismatchException");
    }

    // Question 2: a put of an item over 400 KB fails the whole request, it does not cancel.
    [Test]
    public async Task TransactWriteItems_PutOver400Kilobytes_FailsTheRequest()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var large = new string('x', 400 * 1024);
        var json = $$$$$"""
            {"TransactItems":[
              {"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"large"},"v":{"S":"{{{{{large}}}}}"}}}},
              {"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"small"}}}}]}
            """;

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", json);
        var outcome = Record("2 put over 400 KB in a transaction", Outcome(response), response, "ValidationException");

        // Assert
        await Assert.That(outcome).IsEqualTo("ValidationException");
    }

    // Question 3, alone: an update that fails on its item cancels with a ValidationError for it. More than 38 digits is a number overflow, as a magnitude too large is.
    [Test]
    [Arguments("path into a missing map", "SET m.x = :v", """{":v":{"S":"x"}}""", "ValidationError: The document path provided in the update expression is invalid for update")]
    [Arguments("list_append of a missing list", "SET l = list_append(l, :v)", """{":v":{"L":[{"S":"x"}]}}""", "ValidationError: The provided expression refers to an attribute that does not exist in the item")]
    [Arguments("number over the magnitude", "SET n = :big + :big", """{":big":{"N":"9E+125"}}""", Overflow)]
    [Arguments("number over 38 digits", "SET n = n + :v", """{":v":{"N":"0.1"}}""", Overflow)]
    public async Task TransactWriteItems_UpdateThatFailsOnItsItem_CancelsWithAValidationError(string name, string update, string values, string reason)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await SeedForUpdatesAsync(sandbox);
        var expected = $"{Canceled}[None, {reason}]";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", FailingUpdate(table, "attribute_exists(pk)", update, values));
        var outcome = Record($"3 update that fails on its item, alone: {name}", Outcome(response), response, expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    // Question 3, beside an action that fails: the update is still tried and reported, not None.
    [Test]
    [Arguments("path into a missing map", "SET m.x = :v", """{":v":{"S":"x"}}""", "ValidationError: The document path provided in the update expression is invalid for update")]
    [Arguments("list_append of a missing list", "SET l = list_append(l, :v)", """{":v":{"L":[{"S":"x"}]}}""", "ValidationError: The provided expression refers to an attribute that does not exist in the item")]
    [Arguments("number over 38 digits", "SET n = n + :v", """{":v":{"N":"0.1"}}""", Overflow)]
    public async Task TransactWriteItems_UpdateThatFailsOnItsItemBesideAFailedCondition_CancelsWithBothReasons(string name, string update, string values, string reason)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await SeedForUpdatesAsync(sandbox);
        var expected = $"{Canceled}[ConditionalCheckFailed: The conditional request failed, {reason}]";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", FailingUpdate(table, "attribute_not_exists(pk)", update, values));
        var outcome = Record($"3 update that fails on its item, beside a failed condition: {name}", Outcome(response), response, expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    // Question 4: a key off the schema cancels either transaction, with a ValidationError for that action and None for the others.
    [Test]
    [Arguments("TransactWriteItems", "missing sort key", """{"pk":{"S":"a"}}""")]
    [Arguments("TransactWriteItems", "partition key of another type", """{"pk":{"N":"1"},"sk":{"N":"1"}}""")]
    [Arguments("TransactGetItems", "missing sort key", """{"pk":{"S":"a"}}""")]
    [Arguments("TransactGetItems", "partition key of another type", """{"pk":{"N":"1"},"sk":{"N":"1"}}""")]
    public async Task Transaction_KeyOffTheSchema_CancelsWithAValidationError(string operation, string name, string key)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Number));
        var valid = """{"pk":{"S":"b"},"sk":{"N":"1"}}""";
        var json = operation == "TransactGetItems"
            ? $$$$$"""{"TransactItems":[{"Get":{"TableName":"{{{{{table}}}}}","Key":{{{{{key}}}}}}},{"Get":{"TableName":"{{{{{table}}}}}","Key":{{{{{valid}}}}}}}]}"""
            : $$$$$"""
                {"TransactItems":[
                  {"Update":{"TableName":"{{{{{table}}}}}","Key":{{{{{key}}}}},"UpdateExpression":"SET v = :v","ExpressionAttributeValues":{":v":{"S":"x"}}}},
                  {"Put":{"TableName":"{{{{{table}}}}}","Item":{{{{{valid}}}}}}}]}
                """;
        var expected = $"{Canceled}[{KeyMismatch}, None]";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync(operation, json);
        var outcome = Record($"4 {operation} with a key off the schema: {name}", Outcome(response), response, expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    // Question 5: an update of a key attribute cancels with a ValidationError for that action, in a list of one reason per action.
    [Test]
    public async Task TransactWriteItems_UpdateOfAKeyAttribute_CancelsWithAValidationError()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Number));
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"a"},"sk":{"N":"1"}}}""")).EnsureSuccess();
        var json = $$$$$"""
            {"TransactItems":[
              {"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"b"},"sk":{"N":"1"}}}},
              {"Update":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"a"},"sk":{"N":"1"}},"UpdateExpression":"SET sk = :sk","ExpressionAttributeValues":{":sk":{"N":"2"}}}},
              {"Put":{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"c"},"sk":{"N":"1"}}}}]}
            """;
        var expected = $"{Canceled}[None, ValidationError: One or more parameter values were invalid: Cannot update attribute sk. This attribute is part of the key, None]";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", json);
        var outcome = Record("5 update of a key attribute in a transaction", Outcome(response), response, expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    // Question 6: two forms of one number are one key, read once, and no duplicate.
    [Test]
    public async Task BatchGetItem_KeysThatAreEqualNumbers_ReturnsTheItemOnce()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(PartitionKeyType: KeyAttributeType.Number));
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"N":"1"}}}""")).EnsureSuccess();
        var json = $$$$$"""{"RequestItems":{"{{{{{table}}}}}":{"Keys":[{"pk":{"N":"1"}},{"pk":{"N":"1.0"}}],"ConsistentRead":true}}}""";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("BatchGetItem", json);
        var answer = response.StatusCode == HttpStatusCode.OK
            ? $"OK, {JsonNode.Parse(response.Body)!["Responses"]![table]!.AsArray().Count} items"
            : Outcome(response);
        var outcome = Record("6 BatchGetItem with 1 and 1.0", answer, response, "OK, 1 items");

        // Assert
        await Assert.That(outcome).IsEqualTo("OK, 1 items");
    }

    // Question 7: every number comes back in plain digits, at the top, in a list, in a map and in a set, also at the ends of the range.
    [Test]
    [Arguments("1.0")]
    [Arguments("01.50")]
    [Arguments("1E+2")]
    [Arguments("-0")]
    [Arguments("1E+125")]
    [Arguments("1E-130")]
    public async Task GetItem_StoredNumber_ReturnsItInPlainDigits(string spelling)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = $$$$$"""{"pk":{"S":"n"},"top":{"N":"{{{{{spelling}}}}}"},"list":{"L":[{"N":"{{{{{spelling}}}}}"}]},"map":{"M":{"x":{"N":"{{{{{spelling}}}}}"}}},"set":{"NS":["{{{{{spelling}}}}}"]}}""";
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{{{{{item}}}}}}""")).EnsureSuccess();
        const string Expected = "top=plain digits list=plain digits map=plain digits set=plain digits";

        // Act
        var response = (await sandbox.Emulator.Raw.SendAsync("GetItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"n"}},"ConsistentRead":true}""")).EnsureSuccess();
        var stored = JsonNode.Parse(response.Body)!["Item"]!;
        var returned = new (string Position, string Text)[]
        {
            ("top", stored["top"]!["N"]!.GetValue<string>()),
            ("list", stored["list"]!["L"]![0]!["N"]!.GetValue<string>()),
            ("map", stored["map"]!["M"]!["x"]!["N"]!.GetValue<string>()),
            ("set", stored["set"]!["NS"]![0]!.GetValue<string>()),
        };
        var digits = PlainDigits[spelling];
        var answer = string.Join(' ', returned.Select(position => $"{position.Position}={Form(position.Text, digits, spelling)}"));
        var outcome = Record($"7 number {spelling}", answer, response, Expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(Expected);
    }

    // Question 8: a bare size in any case is a name wherever no parenthesis follows it. The API reference names "Size = :myval" as an expression that fails; run 42361778673
    // evaluated it.
    [Test]
    [Arguments("PutItem", "comparison in a condition", """{"ConditionExpression":"size = :v","ExpressionAttributeValues":{":v":{"S":"x"}},"Item":{"pk":{"S":"a"}}}""", "ConditionalCheckFailedException")]
    [Arguments("PutItem", "argument of attribute_not_exists", """{"ConditionExpression":"attribute_not_exists(size)","Item":{"pk":{"S":"a"}}}""", "OK")]
    [Arguments("UpdateItem", "REMOVE", """{"UpdateExpression":"REMOVE size","Key":{"pk":{"S":"a"}}}""", "OK")]
    [Arguments("GetItem", "projection", """{"ProjectionExpression":"size","Key":{"pk":{"S":"a"}}}""", "OK")]
    [Arguments("PutItem", "Size in a condition", """{"ConditionExpression":"Size = :v","ExpressionAttributeValues":{":v":{"S":"x"}},"Item":{"pk":{"S":"a"}}}""", "ConditionalCheckFailedException")]
    public async Task Expression_BareSizeAsAnAttributeName_IsAName(string operation, string name, string members, string expected)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var request = JsonNode.Parse(members)!.AsObject();
        request["TableName"] = table;

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync(operation, request.ToJsonString());
        var outcome = Record($"8 bare size: {operation} {name}", Outcome(response), response, expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    // Question 9: a page that Limit ends at the last item of a key condition with a sort key part has no LastEvaluatedKey, also with items of the partition beyond it. With
    // a condition on the partition key alone, run 42356511937 had the key for one item and Limit 1, and none for two items, Limit 2 and COUNT, so those cases are recorded.
    [Test]
    [Arguments(1, 1, 1, "ALL_ATTRIBUTES")]
    [Arguments(1, 1, 1, "COUNT")]
    [Arguments(2, 2, 2, "ALL_ATTRIBUTES")]
    [Arguments(3, 2, 2, "ALL_ATTRIBUTES")]
    [Arguments(3, 2, 2, "COUNT")]
    public async Task Query_LimitAtTheLastItemOfTheSortKeyCondition_LeavesOutLastEvaluatedKey(int items, int end, int limit, string select)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await SeedForQueryAsync(sandbox, items);
        var json = QueryJson(table, "pk = :pk AND sk <= :end", $$$$$"""{":pk":{"S":"a"},":end":{"N":"{{{{{end}}}}}"}}""", limit, select);

        // Act
        var response = (await sandbox.Emulator.Raw.SendAsync("Query", json)).EnsureSuccess();
        var outcome = Record($"9 query of sk <= {end} over {items} items with Limit {limit} and {select}", LastKey(response), response, "LastEvaluatedKey absent");

        // Assert
        await Assert.That(outcome).IsEqualTo("LastEvaluatedKey absent");
    }

    // Question 9, the partition key alone: recorded until the rule is clear.
    [Test]
    [Arguments(1, 1, "ALL_ATTRIBUTES")]
    [Arguments(1, 1, "COUNT")]
    [Arguments(2, 2, "ALL_ATTRIBUTES")]
    [Arguments(2, 2, "COUNT")]
    public async Task Query_LimitAtTheLastItemOfThePartition_IsRecorded(int items, int limit, string select)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await SeedForQueryAsync(sandbox, items);
        var json = QueryJson(table, "pk = :pk", """{":pk":{"S":"a"}}""", limit, select);

        // Act
        var response = (await sandbox.Emulator.Raw.SendAsync("Query", json)).EnsureSuccess();
        var outcome = Record($"9 query of pk = :pk over {items} items with Limit {limit} and {select}", LastKey(response), response, null);

        // Assert
        await Assert.That(outcome).IsIn("LastEvaluatedKey present", "LastEvaluatedKey absent");
    }

    // Question 10: the reason of a failed ConditionCheck has no period at the end.
    [Test]
    public async Task TransactWriteItems_FailedConditionCheck_ReasonMessageHasNoPeriod()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TransactItems":[{"ConditionCheck":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"missing"}},"ConditionExpression":"attribute_exists(pk)"}}]}""";
        var expected = $"\"Transaction cancelled, please refer cancellation reasons for specific reasons [ConditionalCheckFailed]\", {Canceled}[ConditionalCheckFailed: The conditional request failed]";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", json);
        var outcome = Record("10 message of a failed ConditionCheck", $"\"{Message(response)}\", {Outcome(response)}", response, expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    // Question 10: a cancelled TransactGetItems has the message of a cancelled TransactWriteItems, and its reasons.
    [Test]
    public async Task TransactGetItems_KeyOfAnotherType_CancelsWithTheMessageAndReasons()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TransactItems":[{"Get":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"N":"1"}}}},{"Get":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"b"}}}}]}""";
        var expected = $"\"Transaction cancelled, please refer cancellation reasons for specific reasons [ValidationError, None]\", {Canceled}[{KeyMismatch}, None]";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactGetItems", json);
        var outcome = Record("10 message of a cancelled TransactGetItems", $"\"{Message(response)}\", {Outcome(response)}", response, expected);

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    // One item for each failing update: a number of 38 digits, and nothing else that the updates reach. The other item is the one the condition check reads.
    private static async Task<string> SeedForUpdatesAsync(Sandbox sandbox)
    {
        var table = await sandbox.CreateTableAsync();
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"target"},"n":{"N":"{{{{{new string('9', 38)}}}}}"}}}""")).EnsureSuccess();
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"other"}}}""")).EnsureSuccess();
        return table;
    }

    private static async Task<string> SeedForQueryAsync(Sandbox sandbox, int items)
    {
        var table = await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Number));
        for (var sk = 1; sk <= items; sk++)
        {
            (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"a"},"sk":{"N":"{{{{{sk}}}}}"}}}""")).EnsureSuccess();
        }

        return table;
    }

    private static string QueryJson(string table, string condition, string values, int limit, string select) => $$$$$"""
        {"TableName":"{{{{{table}}}}}","KeyConditionExpression":"{{{{{condition}}}}}","ExpressionAttributeValues":{{{{{values}}}}},"Limit":{{{{{limit}}}}},"Select":"{{{{{select}}}}}","ConsistentRead":true}
        """;

    private static string LastKey(RawResponse response) =>
        JsonNode.Parse(response.Body)!.AsObject().ContainsKey("LastEvaluatedKey") ? "LastEvaluatedKey present" : "LastEvaluatedKey absent";

    private static string FailingUpdate(string table, string condition, string update, string values) => $$$$$"""
        {"TransactItems":[
          {"ConditionCheck":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"other"}},"ConditionExpression":"{{{{{condition}}}}}"}},
          {"Update":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"target"}},"UpdateExpression":"{{{{{update}}}}}","ExpressionAttributeValues":{{{{{values}}}}}}}]}
        """;

    // "OK", the error code, or for a cancelled transaction its reasons in order, each with its message: "TransactionCanceledException[None, ValidationError: ...]", "[]" for none.
    private static string Outcome(RawResponse response)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return "OK";
        }

        var code = response.ErrorCode;
        if (code != Canceled)
        {
            return code ?? $"HTTP {(int)response.StatusCode}";
        }

        var reasons = JsonNode.Parse(response.Body)!["CancellationReasons"]?.AsArray() ?? [];
        return $"{Canceled}[{string.Join(", ", reasons.Select(static reason => reason!["Message"] is { } message ? $"{reason["Code"]}: {message}" : $"{reason!["Code"]}"))}]";
    }

    // DynamoDB writes "Message" for some errors and "message" for others.
    private static string? Message(RawResponse response)
    {
        var body = JsonNode.Parse(response.Body)!.AsObject();
        return (body["Message"] ?? body["message"])?.GetValue<string>();
    }

    private static string Form(string text, string digits, string spelling) =>
        text == digits ? "plain digits" : text == spelling ? "as written" : text;

    // Writes the answer to the output and the report, and skips the test on an emulator whose answer is not the one of real DynamoDB, or on any emulator for a question that is
    // only recorded (no expected answer). Real DynamoDB and the fake go on to the assertion.
    private string Record(string question, string outcome, RawResponse response, string? expected)
    {
        var line = $"{question}: {backend} answers {outcome}";
        TestContext.Current?.Output.WriteLine($"{line}{Environment.NewLine}{response.Body}");
        if (Environment.GetEnvironmentVariable(ReportVariableName) is { Length: > 0 } path)
        {
            var entry = new JsonObject { ["question"] = question, ["backend"] = backend.ToString(), ["outcome"] = outcome, ["status"] = (int)response.StatusCode, ["body"] = response.Body };
            lock (ReportLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.AppendAllText(path, entry.ToJsonString() + Environment.NewLine);
            }
        }

        if (backend is Backend.Dynoxide or Backend.DynamoDbLocal && (expected is null || outcome != expected))
        {
            Skip.Test($"{line}. Real DynamoDB answers {expected ?? "this question in more than one way"}.");
        }

        return outcome;
    }
}
