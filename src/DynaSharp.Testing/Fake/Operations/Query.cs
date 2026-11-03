using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>Query</c>: one partition of the table or an index, in sort key order or, with <c>ScanIndexForward: false</c>, the other way round, cut into pages by <see cref="PageBuilder"/>.
/// The key condition names the partition with <c>=</c> and may narrow the sort key; an <c>ExclusiveStartKey</c> must lie in what it selects.
/// </summary>
internal static class Query
{
    /// <summary>The operation.</summary>
    public const string Name = "Query";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        ItemRequests.ThrowIfLegacyWithExpressions(request);
        RequestJson.Members(
            request,
            Name,
            "TableName",
            "IndexName",
            "KeyConditionExpression",
            "FilterExpression",
            "ProjectionExpression",
            "ExpressionAttributeNames",
            "ExpressionAttributeValues",
            "Select",
            "Limit",
            "ConsistentRead",
            "ScanIndexForward",
            "ExclusiveStartKey",
            "ReturnConsumedCapacity");
        var read = ReadRequest.Read(db, request);
        var forward = RequestJson.Optional(request, "ScanIndexForward") is not { } forwardValue || RequestJson.ReadBool(forwardValue, "ScanIndexForward");
        var keyCondition = RequestJson.Optional(request, "KeyConditionExpression")
            ?? throw FakeError.Validation("Either the KeyConditions or KeyConditionExpression parameter must be specified in the request.");
        var range = Expressions.ParseKeyCondition(RequestJson.ReadString(keyCondition, "KeyConditionExpression"), read.Scope, read.KeySchema);
        var options = read.ParseOptions(request, read.KeySchema);
        var start = PageBuilder.ReadStartKey(request, read.Table, read.Index);
        if (start is not null)
        {
            ThrowIfOutside(start, range, read.KeySchema);
        }

        read.Write(response, PageBuilder.Build(read.Table.Range(read.Index, range.Partition, range.Sort, forward, start), options));
    }

    private static void ThrowIfOutside(Item start, KeyRange range, KeySchema keySchema)
    {
        if (AttributeComparer.Instance.Compare(start[keySchema.PartitionKeyName], range.Partition) != 0)
        {
            throw FakeError.Validation("The provided starting key is outside query boundaries based on provided conditions");
        }

        if (range.Sort is { } sort && !sort.Admits(start[keySchema.SortKeyName!]))
        {
            throw FakeError.Validation("The provided starting key does not match the range key predicate");
        }
    }
}
