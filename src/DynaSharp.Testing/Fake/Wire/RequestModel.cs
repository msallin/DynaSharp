using System.Collections.Frozen;
using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The checks DynamoDB runs on the model of a request before it reads the request any further: a required member that is missing, the value of an enum, the length of a list or a name, the pattern of a table name.
/// The members that fail are reported together, in the order of the model, as <c>2 validation errors detected: Value 'BOGUS' at 'returnConsumedCapacity' ...; Value 'x!' at 'tableName' ...</c>.
/// Everything after it, a table that is not there, a key that does not fit, an expression, is read only when the model is sound.
/// </summary>
/// <remarks>
/// <para>
/// Which failures DynamoDB reports together is observed, not specified, and the Parity Suite (<c>tier3/validation-ordering</c>) pins what the fake does. A malformed table name of a write is reported alone, ahead of the enums.
/// The enums of a write are reported together, except those of <c>UpdateItem</c>, where only the first is. A write whose key is missing reports everything it finds, in the order of its model. The enums of a batch and of
/// a transaction come together with the table names and the lengths of its lists. The values of an enum are listed in the order DynamoDB lists them, which is none that is evident.
/// </para>
/// <para>
/// A member of the wrong JSON kind is left to the reader that follows, which answers it as a <c>SerializationException</c>.
/// </para>
/// </remarks>
internal static class RequestModel
{
    private const string TableNamePattern = "Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+";
    private const string MapKeys = "Map keys must satisfy constraint: [Member must have length less than or equal to 255, Member must have length greater than or equal to 3, " + TableNamePattern + "]";

    // The least length of a table name in an item operation. DynamoDB takes any name there, and answers a table that does not exist for the short ones.
    private const int ItemTableNameMinimum = 1;

    private static readonly FrozenSet<string> WriteActions = new[] { "Put", "Delete", "Update", "ConditionCheck" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> GetActions = new[] { "Get" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string[]> EnumOrders = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ReturnConsumedCapacity"] = ["INDEXES", "TOTAL", "NONE"],
        ["ReturnItemCollectionMetrics"] = ["SIZE", "NONE"],
        ["ReturnValues"] = ["ALL_NEW", "UPDATED_OLD", "ALL_OLD", "NONE", "UPDATED_NEW"],
        ["Select"] = ["SPECIFIC_ATTRIBUTES", "COUNT", "ALL_ATTRIBUTES", "ALL_PROJECTED_ATTRIBUTES"],
        ["AttributeType"] = ["B", "N", "S"],
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Gets the violation of a value that is none of the values of an enum.</summary>
    /// <param name="value">The value as sent.</param>
    /// <param name="path">The path DynamoDB names, such as <c>returnValues</c> or <c>keySchema.1.member.keyType</c>.</param>
    /// <param name="set">The values of the enum, in the order DynamoDB lists them.</param>
    /// <returns>The violation.</returns>
    public static string EnumViolation(string value, string path, IEnumerable<string> set) =>
        Violation(path, $"Member must satisfy enum value set: [{string.Join(", ", set)}]", value);

    /// <summary>Gets the violation of a required member that is missing.</summary>
    /// <param name="path">The path DynamoDB names.</param>
    /// <returns>The violation.</returns>
    public static string NullViolation(string path) => $"Value null at '{path}' failed to satisfy constraint: Member must not be null";

    /// <summary>Gets a violation.</summary>
    /// <param name="path">The path DynamoDB names.</param>
    /// <param name="constraint">The constraint that failed.</param>
    /// <param name="echoed">The value, which some violations repeat and some do not, or <see langword="null"/> to leave it out.</param>
    /// <returns>The violation.</returns>
    public static string Violation(string path, string constraint, string? echoed = null) =>
        echoed is null ? $"Value at '{path}' failed to satisfy constraint: {constraint}" : $"Value '{echoed}' at '{path}' failed to satisfy constraint: {constraint}";

    /// <summary>Gets the constraint of a length.</summary>
    /// <param name="atLeast"><see langword="true"/> for a least length, <see langword="false"/> for a greatest.</param>
    /// <param name="bound">The length.</param>
    /// <returns>The constraint.</returns>
    public static string Length(bool atLeast, int bound) => $"Member must have length {(atLeast ? "greater than or equal to" : "less than or equal to")} {bound}";

    /// <summary>Gets the values of an enum as DynamoDB lists them.</summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="member">The member of the request, which decides the order DynamoDB lists the values in.</param>
    /// <param name="names">The spellings.</param>
    /// <param name="noneIsDefault"><see langword="true"/> for an enum whose zero member is <c>NONE</c>, which has no entry in the table.</param>
    /// <returns>The values.</returns>
    public static IReadOnlyList<string> EnumSet<TEnum>(string member, EnumWireNames<TEnum> names, bool noneIsDefault)
        where TEnum : struct, Enum
    {
        if (EnumOrders.TryGetValue(member, out var order))
        {
            return order;
        }

        var set = new List<string>();
        if (noneIsDefault)
        {
            set.Add("NONE");
        }

        foreach (var known in Enum.GetValues<TEnum>())
        {
            if (names.TryGetName(known, out var spelling))
            {
                set.Add(spelling);
            }
        }

        return set;
    }

    /// <summary>Checks the name of a table.</summary>
    /// <param name="name">The name.</param>
    /// <param name="path">The path DynamoDB names.</param>
    /// <param name="minimum">The least length: 3 for a table operation, 1 for an item operation.</param>
    /// <returns>The violation, or <see langword="null"/> for a name DynamoDB takes.</returns>
    public static string? TableNameViolation(string name, string path, int minimum)
    {
        if (name.Length < minimum)
        {
            return Violation(path, Length(atLeast: true, minimum), name);
        }

        if (name.Length > 255)
        {
            return Violation(path, Length(atLeast: false, 255), name);
        }

        return name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? null : Violation(path, TableNamePattern, name);
    }

    /// <summary>Checks the model of <c>PutItem</c>, <c>UpdateItem</c> and <c>DeleteItem</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="operation">The operation.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> that lists the violations.</exception>
    public static void CheckItemWrite(JsonElement request, string operation)
    {
        var keyMember = operation == PutItem.Name ? "Item" : "Key";
        var capacity = EnumViolation(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities);
        var table = TableNameViolation(request);
        var metrics = EnumViolation(request, "ReturnItemCollectionMetrics", WireNames.ReturnItemCollectionMetricsValues);
        var returns = EnumViolation(request, "ReturnValues", WireNames.ReturnValues);
        var onFailure = EnumViolation(request, "ReturnValuesOnConditionCheckFailure", WireNames.ReturnValuesOnConditionCheckFailures);
        var key = RequestJson.Optional(request, keyMember) is null ? NullViolation(char.ToLowerInvariant(keyMember[0]) + keyMember[1..]) : null;
        if (key is not null)
        {
            Throw([capacity, table, metrics, returns, onFailure, key]);
        }

        if (table is not null)
        {
            Throw([table]);
        }

        // UpdateItem reports the first enum alone, PutItem and DeleteItem all of them.
        var enums = new[] { capacity, metrics, returns, onFailure };
        Throw(operation == UpdateItem.Name ? enums.Where(static violation => violation is not null).Take(1) : enums);
    }

    /// <summary>Checks the model of <c>Query</c> and <c>Scan</c>.</summary>
    /// <param name="request">The request.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> that lists the violations.</exception>
    public static void CheckRead(JsonElement request)
    {
        var table = TableNameViolation(request);
        Throw([table]);
        var limit = RequestJson.Optional(request, "Limit") is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var count) && count < 1
            ? Violation("Limit", "Member must have value greater than or equal to 1")
            : null;
        Throw([EnumViolation(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities), EnumViolation(request, "Select", WireNames.Selects, noneIsDefault: false), limit]);
    }

    /// <summary>Checks the model of <c>BatchGetItem</c> and <c>BatchWriteItem</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="writes"><see langword="true"/> for <c>BatchWriteItem</c>.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> that lists the violations.</exception>
    public static void CheckBatch(JsonElement request, bool writes)
    {
        var violations = new List<string?>();
        if (RequestJson.Optional(request, "RequestItems") is not { } requestItems)
        {
            violations.Add(NullViolation("requestItems"));
        }
        else if (requestItems.ValueKind == JsonValueKind.Object)
        {
            AddBatchTables(violations, requestItems, writes);
        }

        violations.Add(EnumViolation(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities));
        if (writes)
        {
            violations.Add(EnumViolation(request, "ReturnItemCollectionMetrics", WireNames.ReturnItemCollectionMetricsValues));
        }

        Throw(violations);
    }

    /// <summary>Checks the model of <c>TransactGetItems</c> and <c>TransactWriteItems</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="writes"><see langword="true"/> for <c>TransactWriteItems</c>.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> that lists the violations.</exception>
    public static void CheckTransaction(JsonElement request, bool writes)
    {
        var violations = new List<string?>();
        if (writes && RequestJson.Optional(request, "ClientRequestToken") is { ValueKind: JsonValueKind.String } token)
        {
            var text = token.GetString()!;
            violations.Add(text.Length is < 1 or > TransactWriteItems.MaxTokenLength ? Violation("clientRequestToken", Length(text.Length == 0, text.Length == 0 ? 1 : TransactWriteItems.MaxTokenLength), text) : null);
        }

        violations.Add(EnumViolation(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities));
        if (writes)
        {
            violations.Add(EnumViolation(request, "ReturnItemCollectionMetrics", WireNames.ReturnItemCollectionMetricsValues));
        }

        if (RequestJson.Optional(request, "TransactItems") is not { } items)
        {
            violations.Add(NullViolation("transactItems"));
        }
        else if (items.ValueKind == JsonValueKind.Array)
        {
            AddTransactItems(violations, items, writes);
        }

        Throw(violations);
    }

    /// <summary>Checks the model of <c>CreateTable</c>: the enums of its keys and attributes, the length of its key, and the names of its indexes, the global ones first.</summary>
    /// <param name="request">The request.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> that lists the violations.</exception>
    public static void CheckCreateTable(JsonElement request)
    {
        if (RequestJson.Optional(request, "TableName") is not { } tableName)
        {
            throw FakeError.Validation("The parameter 'TableName' is required but was not present in the request");
        }

        if (tableName.ValueKind == JsonValueKind.String)
        {
            Throw([TableNameViolation(tableName.GetString()!, "tableName", 3)]);
        }

        var violations = new List<string?>();
        AddElementEnums(violations, request, "AttributeDefinitions", "attributeDefinitions", "AttributeType", WireNames.ScalarAttributeTypes);
        AddElementEnums(violations, request, "KeySchema", "keySchema", "KeyType", WireNames.KeyTypes);
        if (RequestJson.Optional(request, "KeySchema") is { ValueKind: JsonValueKind.Array } keySchema && keySchema.GetArrayLength() > 2)
        {
            var elements = keySchema.EnumerateArray().Select(static element => $"KeySchemaElement(attributeName={TextOf(element, "AttributeName")}, keyType={TextOf(element, "KeyType")})");
            violations.Add(Violation("keySchema", Length(atLeast: false, 2), $"[{string.Join(", ", elements)}]"));
        }

        foreach (var (member, path) in new[] { ("GlobalSecondaryIndexes", "globalSecondaryIndexes"), ("LocalSecondaryIndexes", "localSecondaryIndexes") })
        {
            AddIndexNames(violations, request, member, path);
        }

        Throw(violations);
    }

    // The text of a member the way a Java object prints it, which is how DynamoDB echoes a list it rejects.
    private static string TextOf(JsonElement element, string member) =>
        element.ValueKind == JsonValueKind.Object && RequestJson.Optional(element, member) is { ValueKind: JsonValueKind.String } text ? text.GetString()! : "null";

    private static void AddIndexNames(List<string?> violations, JsonElement request, string member, string path)
    {
        if (RequestJson.Optional(request, member) is not { ValueKind: JsonValueKind.Array } indexes)
        {
            return;
        }

        var position = 0;
        foreach (var index in indexes.EnumerateArray())
        {
            position++;
            if (index.ValueKind == JsonValueKind.Object && RequestJson.Optional(index, "IndexName") is { ValueKind: JsonValueKind.String } name)
            {
                violations.Add(TableNameViolation(name.GetString()!, $"{path}.{position}.member.indexName", 3));
            }

            AddElementEnums(violations, index, "KeySchema", $"{path}.{position}.member.keySchema", "KeyType", WireNames.KeyTypes);
        }
    }

    private static void AddElementEnums<TEnum>(List<string?> violations, JsonElement parent, string member, string path, string elementMember, EnumWireNames<TEnum> names)
        where TEnum : struct, Enum
    {
        if (parent.ValueKind != JsonValueKind.Object || RequestJson.Optional(parent, member) is not { ValueKind: JsonValueKind.Array } elements)
        {
            return;
        }

        var position = 0;
        foreach (var element in elements.EnumerateArray())
        {
            position++;
            if (element.ValueKind == JsonValueKind.Object && RequestJson.Optional(element, elementMember) is { ValueKind: JsonValueKind.String } value && !names.TryGetValue(value.GetString()!, out _))
            {
                violations.Add(EnumViolation(value.GetString()!, $"{path}.{position}.member.{char.ToLowerInvariant(elementMember[0])}{elementMember[1..]}", EnumSet(elementMember, names, noneIsDefault: false)));
            }
        }
    }

    private static void AddBatchTables(List<string?> violations, JsonElement requestItems, bool writes)
    {
        var tables = RequestJson.ReadObject(requestItems, "RequestItems");
        if (tables.Count == 0)
        {
            violations.Add(Violation("RequestItems", Length(atLeast: true, 1)));
            return;
        }

        if (tables.Any(static table => TableNameViolation(table.Name, "requestItems", 3) is not null))
        {
            violations.Add(Violation("requestItems", MapKeys, requestItems.GetRawText()));
        }

        var max = writes ? BatchWriteItem.MaxWrites : BatchGetItem.MaxKeys;
        foreach (var table in tables)
        {
            var path = $"RequestItems.{table.Name}.member" + (writes ? string.Empty : ".Keys");
            var list = writes ? table.Value : RequestJson.Optional(table.Value, "Keys") ?? default;
            if (list.ValueKind == JsonValueKind.Array)
            {
                var count = list.GetArrayLength();
                violations.Add(count < 1 ? Violation(path, Length(atLeast: true, 1)) : count > max ? Violation(path, Length(atLeast: false, max)) : null);
            }
        }
    }

    private static void AddTransactItems(List<string?> violations, JsonElement items, bool writes)
    {
        var count = items.GetArrayLength();
        if (count is < 1 or > TransactGetItems.MaxActions)
        {
            violations.Add(Violation("TransactItems", Length(count < 1, count < 1 ? 1 : TransactGetItems.MaxActions)));
        }

        var position = 0;
        foreach (var item in items.EnumerateArray())
        {
            position++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var present = item.EnumerateObject().Where(static member => member.Value.ValueKind != JsonValueKind.Null).ToList();
            if (writes && present.Count != 1)
            {
                violations.Add(Violation("transactItems", "TransactWriteRequest should contain Delete or Put or Update or ConditionCheck", string.Empty));
            }

            // A member the operation does not have is left to the reader, which answers it as one the fake does not support.
            foreach (var action in present.Where(member => member.Value.ValueKind == JsonValueKind.Object && (writes ? WriteActions : GetActions).Contains(member.Name)))
            {
                var path = $"transactItems.{position}.member.{char.ToLowerInvariant(action.Name[0])}{action.Name[1..]}.tableName";
                violations.Add(RequestJson.Optional(action.Value, "TableName") switch
                {
                    null => NullViolation(path),
                    { ValueKind: JsonValueKind.String } name => TableNameViolation(name.GetString()!, path, ItemTableNameMinimum),
                    _ => null,
                });
            }
        }
    }

    private static string? EnumViolation<TEnum>(JsonElement request, string member, EnumWireNames<TEnum> names, bool noneIsDefault = true)
        where TEnum : struct, Enum
    {
        if (RequestJson.Optional(request, member) is not { ValueKind: JsonValueKind.String } value)
        {
            return null;
        }

        var name = value.GetString()!;
        return (noneIsDefault && name == "NONE") || names.TryGetValue(name, out _)
            ? null
            : EnumViolation(name, char.ToLowerInvariant(member[0]) + member[1..], EnumSet(member, names, noneIsDefault));
    }

    private static string? TableNameViolation(JsonElement request) => RequestJson.Optional(request, "TableName") switch
    {
        null => NullViolation("tableName"),
        { ValueKind: JsonValueKind.String } name => TableNameViolation(name.GetString()!, "tableName", ItemTableNameMinimum),
        _ => null,
    };

    private static void Throw(IEnumerable<string?> violations)
    {
        var found = violations.OfType<string>().ToList();
        if (found.Count > 0)
        {
            throw FakeError.Violations(found);
        }
    }
}
