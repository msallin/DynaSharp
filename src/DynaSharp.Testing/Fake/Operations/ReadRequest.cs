using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>What <c>Query</c> and <c>Scan</c> read alike: the table or the index, <c>Limit</c>, <c>Select</c>, <c>ConsistentRead</c>, the capacity to return, the placeholders, and the answer.</summary>
/// <param name="Table">The table.</param>
/// <param name="Index">The index of <c>IndexName</c>, or <see langword="null"/>.</param>
/// <param name="Limit">The <c>Limit</c>, or <see langword="null"/>.</param>
/// <param name="Select">The <c>Select</c> as the request has it.</param>
/// <param name="ConsistentRead">The <c>ConsistentRead</c>.</param>
/// <param name="ReturnConsumedCapacity">The <c>ReturnConsumedCapacity</c>.</param>
/// <param name="Scope">The placeholders of the request.</param>
internal sealed record ReadRequest(FakeTable Table, FakeIndex? Index, int? Limit, SelectKind Select, bool ConsistentRead, ReturnConsumedCapacity ReturnConsumedCapacity, PlaceholderScope Scope)
{
    /// <summary>Gets the key the read goes by: that of the index, or of the table.</summary>
    public KeySchema KeySchema => Index?.KeySchema ?? Table.KeySchema;

    /// <summary>Reads the members both operations have, except the expressions.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The request.</param>
    /// <returns>The read.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c> for the table, a <c>ValidationException</c> for an index the table does not have, a <c>Limit</c> below 1 or a consistent read of a global index.</exception>
    public static ReadRequest Read(FakeDatabase db, JsonElement request)
    {
        ArgumentNullException.ThrowIfNull(db);
        var tableName = TableOperations.ReadTableName(request);
        var indexName = RequestJson.Optional(request, "IndexName") is { } indexValue ? RequestJson.ReadString(indexValue, "IndexName") : null;
        int? limit = RequestJson.Optional(request, "Limit") is { } limitValue ? RequestJson.ReadInt(limitValue, "Limit") : null;
        if (limit < 1)
        {
            throw FakeError.Validation($"1 validation error detected: Value '{limit}' at 'limit' failed to satisfy constraint: Member must have value greater than or equal to 1");
        }

        var select = RequestJson.Optional(request, "Select") is { } selectValue ? RequestJson.ReadEnum(selectValue, "Select", WireNames.Selects) : SelectKind.Unspecified;
        var consistent = RequestJson.Optional(request, "ConsistentRead") is { } consistentValue && RequestJson.ReadBool(consistentValue, "ConsistentRead");
        var capacity = RequestJson.Optional(request, "ReturnConsumedCapacity") is { } capacityValue
            ? RequestJson.ReadEnum(capacityValue, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities, noneIsDefault: true)
            : ReturnConsumedCapacity.None;
        var names = RequestJson.Optional(request, "ExpressionAttributeNames") is { } namesValue ? RequestJson.ReadStringMap(namesValue, "ExpressionAttributeNames") : null;
        var values = RequestJson.Optional(request, "ExpressionAttributeValues") is { } valuesValue ? RequestJson.ReadItem(valuesValue, "ExpressionAttributeValues") : null;
        var table = db.GetTable(tableName);
        var index = indexName is null ? null : table.FindIndex(indexName) ?? throw FakeError.Validation($"The table does not have the specified index: {indexName}");
        if (consistent && index is { IsGlobal: true })
        {
            throw FakeError.Validation("Consistent reads are not supported on global secondary indexes");
        }

        return new ReadRequest(table, index, limit, select, consistent, capacity, new PlaceholderScope(names, values));
    }

    /// <summary>Parses <c>FilterExpression</c> and <c>ProjectionExpression</c> and checks that the expressions of the request used every placeholder.</summary>
    /// <param name="request">The request.</param>
    /// <param name="queried">For a <c>Query</c>, whose key condition the caller parsed with <see cref="Scope"/> already, the key it queries by, which its filter must not read; <see langword="null"/> for a <c>Scan</c>.</param>
    /// <returns>How to cut the page.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects, a filter of a query on a key attribute, a placeholder no expression uses, or the maps in a request without an expression.</exception>
    public PageOptions ParseOptions(JsonElement request, KeySchema? queried)
    {
        var filter = RequestJson.Optional(request, Expressions.FilterExpression) is { } filterValue
            ? Expressions.ParseCondition(RequestJson.ReadString(filterValue, Expressions.FilterExpression), Scope, Expressions.FilterExpression)
            : null;
        if (filter is not null && queried?.Names.FirstOrDefault(filter.AttributeNames.Contains) is { } keyAttribute)
        {
            throw FakeError.Validation($"Filter Expression can only contain non-primary key attributes: Primary key attribute: {keyAttribute}");
        }

        var projection = RequestJson.Optional(request, "ProjectionExpression") is { } projectionValue
            ? Expressions.ParseProjection(RequestJson.ReadString(projectionValue, "ProjectionExpression"), Scope)
            : null;
        if (queried is not null || filter is not null || projection is not null)
        {
            Scope.ThrowIfUnused();
        }
        else
        {
            ItemRequests.ThrowIfMapsWithoutExpression(request);
        }

        return new PageOptions(Limit, filter is null ? null : filter.Evaluate, projection is null ? null : projection.Apply, Select, ConsistentRead, Table, Index);
    }

    /// <summary>Writes the answer: <c>Items</c> unless the request counts only, <c>Count</c>, <c>ScannedCount</c>, <c>LastEvaluatedKey</c> and <c>ConsumedCapacity</c> when asked for.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="page">The page.</param>
    public void Write(Utf8JsonWriter response, Page page)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(page);
        if (Select != SelectKind.Count)
        {
            ResponseJson.WriteItems(response, "Items", page.Items);
        }

        response.WriteNumber("Count"u8, page.Count);
        response.WriteNumber("ScannedCount"u8, page.ScannedCount);
        if (page.LastEvaluatedKey is { } key)
        {
            ResponseJson.WriteItem(response, "LastEvaluatedKey", key);
        }

        var tableUnits = Index is null ? page.Units : page.FetchUnits;
        IndexUnits? indexUnits = Index is null ? null : new IndexUnits(Index.Name, Index.IsGlobal, page.Units);
        ReadCapacity.Write(response, ReturnConsumedCapacity, Table.Name, tableUnits, indexUnits);
    }
}
