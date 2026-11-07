using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// Divergences between the fake and an emulator where the emulator is the one that differs from DynamoDB, or where only real DynamoDB could tell. The list is the counterpart of
/// <see cref="Gaps"/> for generated sequences: each rule says what the emulator does and why it is not the fake that is wrong. A known divergence of a read, or of the consumed capacity
/// alone, leaves the tables alike, so its sequence goes on; any other ends its sequence without failing the test. The open questions are also in <c>spec/tasks.md</c>, for real AWS.
/// </summary>
/// <remarks>
/// Two rules are wide on purpose, and each is covered by the other emulator. Dynoxide skips much of the validation of DynamoDB, so a request the fake rejects and Dynoxide does not is
/// known there, and DynamoDB Local, which validates as DynamoDB does, holds the fake to it. DynamoDB Local reports capacity by rules of its own, so capacity alone is known there, and
/// Dynoxide, which follows the Developer Guide, holds the fake to it. A run of both emulators, as CI does it, misses neither.
/// </remarks>
internal static class KnownDivergences
{
    private const string DynamoDbLocalCapacity =
        "DynamoDB Local reports capacity by rules of its own (0 units for a read that finds nothing, an index write for an item outside the index, 1 unit per transactional write); Dynoxide holds the fake to the Developer Guide.";

    private const string DynamoDbLocalNumbers =
        "DynamoDB Local returns some numbers in the form they were written (1.0, 01.50, 1E+2, -0, 1E-130), where the fake and Dynoxide return them normalized.";

    private const string DynamoDbLocalWholeTransaction =
        "DynamoDB Local fails the whole TransactWriteItems for a key that does not fit the schema, a put without a key attribute or an update that fails on its item, where the fake and Dynoxide cancel with a ValidationError reason. Open for real AWS.";

    private const string DynamoDbLocalKeyUpdateInTransaction =
        "DynamoDB Local cancels a TransactWriteItems that updates a key attribute, after the conditions; the fake rejects the request, and Dynoxide applies the update. Open for real AWS.";

    private const string DynamoDbLocalPeriod = "DynamoDB Local ends the message of a failed ConditionCheck of a transaction with a period. Open for real AWS.";

    private const string DynamoDbLocalReasonsMissing = "DynamoDB Local cancels a TransactGetItems whose key does not fit the schema without CancellationReasons.";

    private const string DynamoDbLocalSize =
        "DynamoDB Local takes a bare size as an attribute name outside a function call; the fake and Dynoxide reject it as a reserved word. Open for real AWS.";

    private const string DynamoDbLocalEmptyProjection = "DynamoDB Local answers {\"Item\":{}} for a TransactGetItems projection that matches no attribute of an item; the fake and Dynoxide answer {}, as the Parity Suite recorded of DynamoDB.";

    private const string DynamoDbLocalSameItemInTransaction = "DynamoDB Local takes two actions on keys that are equal numbers in another form (1 and 1.0) as two items in a TransactWriteItems; the fake and Dynoxide reject them as one item twice.";

    private const string NumericDuplicateKeys = "Both emulators take keys that are equal numbers in another form (1 and 1.0) as two keys in a BatchGetItem; the fake rejects them as duplicates. Open for real AWS.";

    private const string TokenReplay = "Both emulators take a replay with another ReturnConsumedCapacity as the same request; the fake answers IdempotentParameterMismatchException. Open for real AWS.";

    private const string DynoxideValidation =
        "Dynoxide skips validation DynamoDB does (filters and projections an index cannot serve, operand types, BETWEEN bounds, attribute_type names, an empty key prefix, an empty key in TransactGetItems, an update of a key attribute in a transaction); DynamoDB Local holds the fake to it.";

    private const string DynoxideTransactionCapacity =
        "Dynoxide adds ReadCapacityUnits or WriteCapacityUnits to the TOTAL capacity of a transaction, and counts an index write of a transaction once; DynamoDB Local agrees with the fake on the units.";

    private const string DynoxideSetDelete = "Dynoxide matches the numbers a DELETE takes out of a number set by their text, so -0 leaves 0 and 1.0 leaves 1 in the set; the fake and DynamoDB Local take them out.";

    private const string DynoxideOldImageOfANewItem = "Dynoxide returns the key as the ALL_OLD image of an item an update creates, where there was no item.";

    private const string DynoxideTransactGetProjection = "Dynoxide adds the key attributes to the items TransactGetItems projects (Gap.TransactGetProjectionWithoutKeys).";

    private const string DynoxideCancelledUpdates =
        "Dynoxide tries the updates of a cancelled transaction and reports a ValidationError for one that fails on its item, where the fake reports None; DynamoDB Local fails the request. Open for real AWS.";

    private const string DynamoDbLocalLastPage =
        "DynamoDB Local leaves out the LastEvaluatedKey of a query page that a Limit ends at the last item of the key range; the fake and Dynoxide send it, and an empty page follows. Open for real AWS.";

    private const string KeyFaultOrder =
        "A transaction with a key off the schema in one action and another fault elsewhere (an empty key, one item twice): the fake cancels for the key, the emulators fail the request for the other fault. Which fault DynamoDB names first is fine print the fake does not mimic.";

    private const string DynoxideIndexScanFilter =
        "Dynoxide applies the filter of a scan of an index to the projected attributes; the fake and DynamoDB Local apply it to the table item.";

    private const string ValidationException = "ValidationException";

    private const string Canceled = "TransactionCanceledException";

    private static readonly string[] DynamoDbLocalTransactionMessages =
    [
        "The number of conditions on the keys is invalid",
        "One of the required keys was not given a value",
        "The provided expression refers to an attribute that does not exist in the item",
        "The document path provided in the update expression is invalid for update",
        "An operand in the update expression has an incorrect data type",
        "DynamoDB only supports precision up to 38 digits",
        "Number overflow",
        "Number underflow",
    ];

    private static readonly string[] KeyFaults =
    [
        "The provided key element does not match the schema",
        "One or more parameter values were invalid: Type mismatch for key",
        "One or more parameter values were invalid: Missing the key",
    ];

    private static readonly (Backend? Backend, string Reason, Func<Divergence, bool> Matches)[] Rules =
    [
        (Backend.DynamoDbLocal, DynamoDbLocalCapacity, static d => d.IsCapacityOnly),
        (Backend.DynamoDbLocal, DynamoDbLocalNumbers, static d => d.Fake.ErrorCode == d.Emulator.ErrorCode && Numbers.Normalized(d.Fake.Result) == Numbers.Normalized(d.Emulator.Result)),
        (Backend.DynamoDbLocal, DynamoDbLocalWholeTransaction, static d => d.Operation.Name == "TransactWriteItems" && d.Fake.ErrorCode == Canceled
            && d.Emulator.ErrorCode == ValidationException && d.Emulator.Messages.Any(static message => DynamoDbLocalTransactionMessages.Any(message.StartsWith))),
        (Backend.DynamoDbLocal, DynamoDbLocalKeyUpdateInTransaction, static d => d.Operation.Name == "TransactWriteItems" && IsKeyUpdate(d.Fake) && d.Emulator.ErrorCode == Canceled),
        (Backend.DynamoDbLocal, DynamoDbLocalPeriod, static d => d.Fake.ErrorCode == Canceled
            && Numbers.Normalized(d.Emulator.Result.Replace("The conditional request failed.", "The conditional request failed", StringComparison.Ordinal)) == Numbers.Normalized(d.Fake.Result)),
        (Backend.DynamoDbLocal, DynamoDbLocalReasonsMissing, static d => d.Operation.Name == "TransactGetItems" && d.Fake.ErrorCode is Canceled or ValidationException
            && d.Emulator.ErrorCode == Canceled && !d.Emulator.Result.Contains("CancellationReasons", StringComparison.Ordinal)),
        (Backend.DynamoDbLocal, DynamoDbLocalSize, static d => d.Fake.ErrorCode == ValidationException && d.Emulator.ErrorCode != ValidationException
            && d.Fake.Messages.Any(static message => message.EndsWith("reserved keyword: size", StringComparison.Ordinal))),
        (Backend.DynamoDbLocal, DynamoDbLocalEmptyProjection, static d => d.Operation.Name == "TransactGetItems" && d.Fake.ErrorCode is null && d.Emulator.ErrorCode is null
            && d.Emulator.Result.Replace("{\"Item\":{}}", "{}", StringComparison.Ordinal) == d.Fake.Result),
        (Backend.DynamoDbLocal, DynamoDbLocalSameItemInTransaction, static d => d.Operation.Name == "TransactWriteItems" && d.Emulator.ErrorCode != ValidationException
            && d.Fake.Messages.Contains("Transaction request cannot include multiple operations on one item")),
        (Backend.DynamoDbLocal, DynamoDbLocalLastPage, static d => d.Operation.Reading == Reading.Pages && d.Fake.ErrorCode is null && d.Emulator.ErrorCode is null
            && WithoutEmptyLastPage(d.Fake.Result) == d.Emulator.Result),
        (null, KeyFaultOrder, static d => d.Operation.Name.StartsWith("Transact", StringComparison.Ordinal) && d.Fake.ErrorCode == Canceled && d.Emulator.ErrorCode == ValidationException
            && d.Fake.Messages.Any(static message => KeyFaults.Any(message.StartsWith))),
        (null, NumericDuplicateKeys, static d => d.Operation.Name == "BatchGetItem" && d.Emulator.ErrorCode is null && d.Fake.Messages.Contains("Provided list of item keys contains duplicates")),
        (null, TokenReplay, static d => d.Fake.ErrorCode == "IdempotentParameterMismatchException" && d.Emulator.ErrorCode is null),
        (Backend.Dynoxide, DynoxideValidation, static d => d.Fake.ErrorCode == ValidationException && d.Emulator.ErrorCode != ValidationException),
        (Backend.Dynoxide, DynoxideTransactionCapacity, static d => d.IsCapacityOnly && d.Operation.Name.StartsWith("Transact", StringComparison.Ordinal)),
        (Backend.Dynoxide, DynoxideIndexScanFilter, static d => d.Operation.Name == "Scan" && d.Operation.Body["IndexName"] is not null && d.Operation.Body["FilterExpression"] is not null
            && d.Fake.ErrorCode is null && d.Emulator.ErrorCode is null),
        (Backend.Dynoxide, DynoxideSetDelete, static d => d.Fake.ErrorCode is null && d.Emulator.ErrorCode is null
            && Numbers.Normalized(d.Fake.Result, emptyNumberSets: true) == Numbers.Normalized(d.Emulator.Result, emptyNumberSets: true)),
        (Backend.Dynoxide, DynoxideOldImageOfANewItem, static d => d.Operation.Name == "UpdateItem" && d.Operation.Body["ReturnValues"]?.GetValue<string>() == "ALL_OLD"
            && d.Fake.ErrorCode is null && d.Fake.Result == "{}" && d.Emulator.ErrorCode is null),
        (Backend.Dynoxide, DynoxideTransactGetProjection, static d => d.Operation.Name == "TransactGetItems" && d.Fake.ErrorCode is null && d.Emulator.ErrorCode is null
            && d.Operation.Body["TransactItems"]!.AsArray().Any(static get => get!["Get"]!["ProjectionExpression"] is not null)),
        (Backend.Dynoxide, DynoxideCancelledUpdates, static d => d.Operation.Name == "TransactWriteItems" && d.Fake.ErrorCode == Canceled && d.Emulator.ErrorCode == Canceled
            && OnlyValidationErrorsForNone(d.Fake.Result, d.Emulator.Result)),
    ];

    /// <summary>Explains a divergence, if it is a known one.</summary>
    /// <param name="backend">The emulator.</param>
    /// <param name="divergence">The divergence.</param>
    /// <returns>The reason, or <see langword="null"/> for a divergence nobody explained yet.</returns>
    public static string? Explain(Backend backend, Divergence divergence) =>
        Rules.FirstOrDefault(rule => (rule.Backend is null || rule.Backend == backend) && rule.Matches(divergence)).Reason;

    private static bool IsKeyUpdate(Outcome outcome) =>
        outcome.ErrorCode == ValidationException && outcome.Messages.Any(static message => message.Contains("This attribute is part of the key", StringComparison.Ordinal));

    // The pages of the fake without a last page that holds nothing, and without the LastEvaluatedKey that led to it.
    private static string WithoutEmptyLastPage(string result)
    {
        var pages = JsonNode.Parse(result)!["Pages"]!.AsArray();
        if (pages.Count < 2 || pages[^1]!["Count"]!.GetValue<long>() != 0 || pages[^1]!["ScannedCount"]!.GetValue<long>() != 0)
        {
            return result;
        }

        pages.RemoveAt(pages.Count - 1);
        pages[^1]!.AsObject().Remove("LastEvaluatedKey");
        return Canonical.Of(new JsonObject { ["Pages"] = pages.DeepClone() });
    }

    // The reasons are alike, but where the fake has None, Dynoxide may have ValidationError.
    private static bool OnlyValidationErrorsForNone(string fake, string emulator)
    {
        var fakeReasons = JsonNode.Parse(fake)!["CancellationReasons"]?.AsArray();
        var emulatorReasons = JsonNode.Parse(emulator)!["CancellationReasons"]?.AsArray();
        if (fakeReasons is null || emulatorReasons is null || fakeReasons.Count != emulatorReasons.Count)
        {
            return false;
        }

        var differs = false;
        for (var i = 0; i < fakeReasons.Count; i++)
        {
            var mine = fakeReasons[i]!["Code"]!.GetValue<string>();
            var theirs = emulatorReasons[i]!["Code"]!.GetValue<string>();
            if (mine == theirs)
            {
                continue;
            }

            if (mine != "None" || theirs != "ValidationError")
            {
                return false;
            }

            differs = true;
        }

        return differs;
    }

    /// <summary>Puts every number of DynamoDB JSON into one form, for a comparison that ignores how a number is written.</summary>
    private static class Numbers
    {
        /// <summary>Gets the canonical text of DynamoDB JSON with its numbers in one form, and the items of a set of items (<c>Items</c>, <c>Responses</c>) sorted again.</summary>
        /// <param name="json">The JSON.</param>
        /// <param name="emptyNumberSets">Whether every number set is taken as empty, for a comparison of all but what number sets hold.</param>
        /// <returns>The text.</returns>
        public static string Normalized(string json, bool emptyNumberSets = false) => Canonical.Of(Walk(JsonNode.Parse(json), emptyNumberSets));

        private static JsonNode? Walk(JsonNode? node, bool emptySets)
        {
            switch (node)
            {
                case JsonObject value when value.Count == 1 && value["N"] is JsonValue number && number.TryGetValue<string>(out var text):
                    return new JsonObject { ["N"] = Form(text) };
                case JsonObject value when value.Count == 1 && value["NS"] is JsonArray set:
                    var elements = set.Select(static element => Form(element!.GetValue<string>())).Where(_ => !emptySets).Order(StringComparer.Ordinal);
                    return new JsonObject { ["NS"] = new JsonArray([.. elements.Select(static element => (JsonNode)element)]) };
                case JsonObject value:
                    return new JsonObject(value.Select(pair => KeyValuePair.Create(pair.Key, Member(pair.Key, pair.Value, emptySets))));
                case JsonArray array:
                    return new JsonArray([.. array.Select(element => Walk(element, emptySets))]);
                default:
                    return node?.DeepClone();
            }
        }

        private static JsonNode? Member(string name, JsonNode? value, bool emptySets)
        {
            if (name is not ("Items" or "Responses") || value is not JsonArray items)
            {
                return Walk(value, emptySets);
            }

            var sorted = items.Select(item => Canonical.Of(Walk(item, emptySets))).Order(StringComparer.Ordinal);
            return new JsonArray([.. sorted.Select(static text => JsonNode.Parse(text))]);
        }

        // The value of a number as significant digits and an exponent: 1.0, 01.50 and 1E+0 are "1E0", "15E-1" and "1E0".
        private static string Form(string text)
        {
            var exponent = 0;
            var mantissa = text;
            var e = text.IndexOfAny(['e', 'E']);
            if (e >= 0)
            {
                exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                mantissa = text[..e];
            }

            var point = mantissa.IndexOf('.', StringComparison.Ordinal);
            if (point >= 0)
            {
                exponent -= mantissa.Length - point - 1;
                mantissa = mantissa.Remove(point, 1);
            }

            var digits = BigInteger.Parse(mantissa, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (digits.IsZero)
            {
                return "0";
            }

            while (digits % 10 == 0)
            {
                digits /= 10;
                exponent++;
            }

            return string.Create(CultureInfo.InvariantCulture, $"{digits}E{exponent}");
        }
    }
}
