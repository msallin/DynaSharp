using System.Net;
using System.Text.Json.Nodes;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Fidelity;

/// <summary>
/// The questions about DynamoDB that <c>FakeDynamoDb</c>, DynamoDB Local and Dynoxide answer differently (<c>spec/tasks.md</c>, R20), asked of real DynamoDB on the raw wire.
/// Where the AWS documentation states the answer, a test asserts it; where it is silent, the test asserts one of the answers the backends gave, so the first run on real AWS
/// tells which one holds. Either way the answer is written to the output of the test and, when <c>DYNASHARP_FIDELITY_REPORT</c> names a file, to that file as one JSON line.
/// </summary>
/// <remarks>
/// Only real DynamoDB is asserted. Every other backend sends the same requests, records its answer, and is skipped with the answer in the reason, so a run on the emulators shows
/// where each one stands without failing. Once real AWS has answered, the answer is the spec of the fake: a test here then pins it, and the fake follows.
/// </remarks>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class FidelityTests(Backend backend)
{
    /// <summary>The name of the environment variable that names the report file.</summary>
    public const string ReportVariableName = "DYNASHARP_FIDELITY_REPORT";

    private const string Canceled = "TransactionCanceledException";

    private static readonly Lock ReportLock = new();

    // What each spelling is in plain digits, the form the fake returns.
    private static readonly Dictionary<string, string> PlainDigits = new(StringComparer.Ordinal)
    {
        ["1.0"] = "1",
        ["01.50"] = "1.5",
        ["1E+2"] = "100",
        ["-0"] = "0",
        ["1E+125"] = "1" + new string('0', 125),
        ["1E-130"] = "0." + new string('0', 129) + "1",
    };

    // Question 1. The documentation: "If you submit a request with the same client token but a change in other parameters within the 10-minute idempotency window,
    // DynamoDB returns an IdempotentParameterMismatch exception." It also says that a repeat returns other capacity, so whether ReturnConsumedCapacity counts is the question.
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
        var outcome = Outcome(response);
        Record("1 token with another ReturnConsumedCapacity", outcome, response);

        // Assert
        await Assert.That(outcome).IsEqualTo("IdempotentParameterMismatchException");
    }

    // Question 2. The documentation lists "an item size becomes too large (larger than 400 KB) ... because of changes made by the transaction" as a cancellation, and also
    // rejects requests as a whole; which one a put of a too large item is, it does not say.
    [Test]
    public async Task TransactWriteItems_PutOver400Kilobytes_FailsTheRequestOrCancels()
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
        var outcome = Outcome(response);
        Record("2 put over 400 KB in a transaction", outcome, response);

        // Assert
        await Assert.That(outcome).IsIn("ValidationException", $"{Canceled}[ValidationError,None]");
    }

    // Question 3, alone. The documentation lists the messages of a ValidationError reason, among them "The document path provided in the update expression is invalid for
    // update", "The provided expression refers to an attribute that does not exist in the item" and "Number overflow. Attempting to store a number with magnitude larger than
    // supported range", so those cases are asserted. More than 38 significant digits is not in the list, and is recorded.
    [Test]
    [Arguments("path into a missing map", "SET m.x = :v", """{":v":{"S":"x"}}""", true)]
    [Arguments("list_append of a missing list", "SET l = list_append(l, :v)", """{":v":{"L":[{"S":"x"}]}}""", true)]
    [Arguments("number over the magnitude", "SET n = :big + :big", """{":big":{"N":"9E+125"}}""", true)]
    [Arguments("number over 38 digits", "SET n = n + :v", """{":v":{"N":"0.1"}}""", false)]
    public async Task TransactWriteItems_UpdateThatFailsOnItsItem_CancelsWithAValidationError(string name, string update, string values, bool documented)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await SeedForUpdatesAsync(sandbox);

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", FailingUpdate(table, "attribute_exists(pk)", update, values));
        var outcome = Outcome(response);
        Record($"3 update that fails on its item, alone: {name}", outcome, response);

        // Assert
        string[] expected = documented ? [$"{Canceled}[None,ValidationError]"] : ["ValidationException", $"{Canceled}[None,ValidationError]", "OK"];
        await Assert.That(outcome).IsIn(expected);
    }

    // Question 3, beside an action that fails: is the update still reported, or None because the transaction was cancelled before it?
    [Test]
    [Arguments("path into a missing map", "SET m.x = :v", """{":v":{"S":"x"}}""")]
    [Arguments("list_append of a missing list", "SET l = list_append(l, :v)", """{":v":{"L":[{"S":"x"}]}}""")]
    [Arguments("number over 38 digits", "SET n = n + :v", """{":v":{"N":"0.1"}}""")]
    public async Task TransactWriteItems_UpdateThatFailsOnItsItemBesideAFailedCondition_IsRecorded(string name, string update, string values)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await SeedForUpdatesAsync(sandbox);

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", FailingUpdate(table, "attribute_not_exists(pk)", update, values));
        var outcome = Outcome(response);
        Record($"3 update that fails on its item, beside a failed condition: {name}", outcome, response);

        // Assert
        await Assert.That(outcome).IsIn("ValidationException", $"{Canceled}[ConditionalCheckFailed,ValidationError]", $"{Canceled}[ConditionalCheckFailed,None]");
    }

    // Question 4. The documentation says that "a user error, such as an invalid data format" cancels both operations, and does not say whether a key off the schema is one.
    [Test]
    [Arguments("TransactWriteItems", "missing sort key", """{"pk":{"S":"a"}}""")]
    [Arguments("TransactWriteItems", "partition key of another type", """{"pk":{"N":"1"},"sk":{"N":"1"}}""")]
    [Arguments("TransactGetItems", "missing sort key", """{"pk":{"S":"a"}}""")]
    [Arguments("TransactGetItems", "partition key of another type", """{"pk":{"N":"1"},"sk":{"N":"1"}}""")]
    public async Task Transaction_KeyOffTheSchema_FailsTheRequestOrCancels(string operation, string name, string key)
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

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync(operation, json);
        var outcome = Outcome(response);
        Record($"4 {operation} with a key off the schema: {name}", outcome, response);

        // Assert
        await Assert.That(outcome).IsIn("ValidationException", $"{Canceled}[ValidationError,None]", $"{Canceled}[]");
    }

    // Question 5. The documentation of UpdateItem says a key attribute cannot be updated, so applying it is wrong; how a transaction refuses it, it does not say. The update is
    // the second of three actions, so a list of one reason tells itself apart from a list in the order of the actions.
    [Test]
    public async Task TransactWriteItems_UpdateOfAKeyAttribute_FailsTheRequestOrCancels()
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

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", json);
        var outcome = Outcome(response);
        Record("5 update of a key attribute in a transaction", outcome, response);

        // Assert
        await Assert.That(outcome).IsIn("ValidationException", $"{Canceled}[None,ValidationError,None]", $"{Canceled}[ValidationError]");
    }

    // Question 6. The documentation rejects duplicate keys in BatchGetItem, and does not say whether two spellings of one number are duplicates.
    [Test]
    public async Task BatchGetItem_KeysThatAreEqualNumbers_IsRecorded()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(PartitionKeyType: KeyAttributeType.Number));
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"N":"1"}}}""")).EnsureSuccess();
        var json = $$$$$"""{"RequestItems":{"{{{{{table}}}}}":{"Keys":[{"pk":{"N":"1"}},{"pk":{"N":"1.0"}}],"ConsistentRead":true}}}""";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("BatchGetItem", json);
        var outcome = response.StatusCode == HttpStatusCode.OK
            ? $"OK, {JsonNode.Parse(response.Body)!["Responses"]![table]!.AsArray().Count} items"
            : Outcome(response);
        Record("6 BatchGetItem with 1 and 1.0", outcome, response);

        // Assert
        await Assert.That(outcome).IsIn("ValidationException", "OK, 1 items", "OK, 2 items");
    }

    // Question 7. The documentation: "Leading and trailing zeroes are trimmed." That holds 1.0 and 01.50 everywhere. The earlier runs on real AWS saw -0 and exponents in
    // plain digits at the top level (NumberFormatTests); in lists, maps and sets, and for the limits of the range, the answer is recorded: plain digits or the form written.
    [Test]
    [Arguments("1.0", true, true)]
    [Arguments("01.50", true, true)]
    [Arguments("1E+2", true, false)]
    [Arguments("-0", true, false)]
    [Arguments("1E+125", false, false)]
    [Arguments("1E-130", false, false)]
    public async Task GetItem_StoredNumber_ReturnsItInPlainDigits(string spelling, bool documentedAtTheTop, bool documentedNested)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = $$$$$"""{"pk":{"S":"n"},"top":{"N":"{{{{{spelling}}}}}"},"list":{"L":[{"N":"{{{{{spelling}}}}}"}]},"map":{"M":{"x":{"N":"{{{{{spelling}}}}}"}}},"set":{"NS":["{{{{{spelling}}}}}"]}}""";
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{{{{{item}}}}}}""")).EnsureSuccess();

        // Act
        var response = (await sandbox.Emulator.Raw.SendAsync("GetItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"n"}},"ConsistentRead":true}""")).EnsureSuccess();
        var stored = JsonNode.Parse(response.Body)!["Item"]!;
        var returned = new (string Position, bool Documented, string Text)[]
        {
            ("top", documentedAtTheTop, stored["top"]!["N"]!.GetValue<string>()),
            ("list", documentedNested, stored["list"]!["L"]![0]!["N"]!.GetValue<string>()),
            ("map", documentedNested, stored["map"]!["M"]!["x"]!["N"]!.GetValue<string>()),
            ("set", documentedNested, stored["set"]!["NS"]![0]!.GetValue<string>()),
        };
        var digits = PlainDigits[spelling];
        var outcome = string.Join(' ', returned.Select(position => $"{position.Position}={Form(position.Text, digits, spelling)}"));
        Record($"7 number {spelling}", outcome, response);

        // Assert
        var unexpected = returned.Where(position => position.Text != digits && (position.Documented || position.Text != spelling)).Select(static position => $"{position.Position}={position.Text}");
        await Assert.That(unexpected).IsEmpty();
    }

    // Question 8. The documentation lists SIZE among the reserved words, and the API reference names "Size = :myval" as an expression that fails for that reason.
    [Test]
    [Arguments("PutItem", "comparison in a condition", """{"ConditionExpression":"size = :v","ExpressionAttributeValues":{":v":{"S":"x"}},"Item":{"pk":{"S":"a"}}}""")]
    [Arguments("PutItem", "argument of attribute_not_exists", """{"ConditionExpression":"attribute_not_exists(size)","Item":{"pk":{"S":"a"}}}""")]
    [Arguments("UpdateItem", "REMOVE", """{"UpdateExpression":"REMOVE size","Key":{"pk":{"S":"a"}}}""")]
    [Arguments("GetItem", "projection", """{"ProjectionExpression":"size","Key":{"pk":{"S":"a"}}}""")]
    public async Task Expression_BareSizeAsAnAttributeName_ThrowsValidationException(string operation, string name, string members)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var request = JsonNode.Parse(members)!.AsObject();
        request["TableName"] = table;

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync(operation, request.ToJsonString());
        var outcome = Outcome(response);
        Record($"8 bare size: {operation} {name}", outcome, response);

        // Assert
        await Assert.That(outcome).IsEqualTo("ValidationException");
    }

    // Question 9. The documentation of Limit: "If DynamoDB processes the number of items up to the limit ..., it stops the operation and returns the matching values up to
    // that point, and a key in LastEvaluatedKey", also when nothing follows: neither in the partition, nor in the range of the sort key condition, with items beyond it.
    [Test]
    [Arguments(1, null, 1, "ALL_ATTRIBUTES")]
    [Arguments(2, null, 2, "COUNT")]
    [Arguments(1, 1, 1, "ALL_ATTRIBUTES")]
    [Arguments(1, 1, 1, "COUNT")]
    [Arguments(2, 2, 2, "ALL_ATTRIBUTES")]
    [Arguments(3, 2, 2, "ALL_ATTRIBUTES")]
    [Arguments(3, 2, 2, "COUNT")]
    public async Task Query_LimitEqualToTheItemsLeft_ReturnsALastEvaluatedKey(int items, int? end, int limit, string select)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Number));
        for (var sk = 1; sk <= items; sk++)
        {
            (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"a"},"sk":{"N":"{{{{{sk}}}}}"}}}""")).EnsureSuccess();
        }

        var condition = end is null ? "pk = :pk" : "pk = :pk AND sk <= :end";
        var values = end is null ? """{":pk":{"S":"a"}}""" : $$$$$"""{":pk":{"S":"a"},":end":{"N":"{{{{{end}}}}}"}}""";
        var json = $$$$$"""
            {"TableName":"{{{{{table}}}}}","KeyConditionExpression":"{{{{{condition}}}}}","ExpressionAttributeValues":{{{{{values}}}}},"Limit":{{{{{limit}}}}},"Select":"{{{{{select}}}}}","ConsistentRead":true}
            """;

        // Act
        var response = (await sandbox.Emulator.Raw.SendAsync("Query", json)).EnsureSuccess();
        var outcome = JsonNode.Parse(response.Body)!.AsObject().ContainsKey("LastEvaluatedKey") ? "LastEvaluatedKey present" : "LastEvaluatedKey absent";
        Record($"9 query of {condition} over {items} items with Limit {limit} and {select}", outcome, response);

        // Assert
        await Assert.That(outcome).IsEqualTo("LastEvaluatedKey present");
    }

    // Question 10, the reason of a ConditionCheck. The documentation prints the message as "The conditional request failed.", in a list where every message ends with a period.
    [Test]
    public async Task TransactWriteItems_FailedConditionCheck_ReasonMessageIsRecorded()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TransactItems":[{"ConditionCheck":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"missing"}},"ConditionExpression":"attribute_exists(pk)"}}]}""";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactWriteItems", json);
        var reason = JsonNode.Parse(response.Body)!["CancellationReasons"]?[0]?["Message"]?.GetValue<string>();
        Record("10 message of a failed ConditionCheck", $"\"{reason}\" in \"{Message(response)}\"", response);

        // Assert
        await Assert.That(reason).IsIn("The conditional request failed", "The conditional request failed.");
    }

    // Question 10, the cancellation of TransactGetItems: its message and reasons, with the key of another type of question 4.
    [Test]
    public async Task TransactGetItems_KeyOfAnotherType_MessageAndReasonsAreRecorded()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = $$$$$"""{"TransactItems":[{"Get":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"N":"1"}}}},{"Get":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"b"}}}}]}""";

        // Act
        var response = await sandbox.Emulator.Raw.SendAsync("TransactGetItems", json);
        var reasons = JsonNode.Parse(response.Body)!["CancellationReasons"]?.ToJsonString() ?? "no CancellationReasons";
        Record("10 message of a cancelled TransactGetItems", $"{response.ErrorCode}: \"{Message(response)}\", {reasons}", response);

        // Assert
        await Assert.That(response.ErrorCode).IsIn("ValidationException", Canceled);
    }

    // One item for each failing update: a number of 38 digits, and nothing else that the updates reach. The other item is the one the condition check reads.
    private static async Task<string> SeedForUpdatesAsync(Sandbox sandbox)
    {
        var table = await sandbox.CreateTableAsync();
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"target"},"n":{"N":"{{{{{new string('9', 38)}}}}}"}}}""")).EnsureSuccess();
        (await sandbox.Emulator.Raw.SendAsync("PutItem", $$$$$"""{"TableName":"{{{{{table}}}}}","Item":{"pk":{"S":"other"}}}""")).EnsureSuccess();
        return table;
    }

    private static string FailingUpdate(string table, string condition, string update, string values) => $$$$$"""
        {"TransactItems":[
          {"ConditionCheck":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"other"}},"ConditionExpression":"{{{{{condition}}}}}"}},
          {"Update":{"TableName":"{{{{{table}}}}}","Key":{"pk":{"S":"target"}},"UpdateExpression":"{{{{{update}}}}}","ExpressionAttributeValues":{{{{{values}}}}}}}]}
        """;

    // "OK", the error code, or for a cancelled transaction the codes of its reasons in order: "TransactionCanceledException[None,ValidationError]", "[]" for none.
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
        return $"{Canceled}[{string.Join(',', reasons.Select(static reason => reason!["Code"]!.GetValue<string>()))}]";
    }

    // DynamoDB writes "Message" for some errors and "message" for others.
    private static string? Message(RawResponse response)
    {
        var body = JsonNode.Parse(response.Body)!.AsObject();
        return (body["Message"] ?? body["message"])?.GetValue<string>();
    }

    private static string Form(string text, string digits, string spelling) =>
        text == digits ? "plain digits" : text == spelling ? "as written" : text;

    // Writes the answer to the output and the report, and skips the test on every backend but real DynamoDB, which alone is asserted.
    private void Record(string question, string outcome, RawResponse response)
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

        if (backend != Backend.RealAws)
        {
            Skip.Test($"{line}. Only real DynamoDB is asserted.");
        }
    }
}
