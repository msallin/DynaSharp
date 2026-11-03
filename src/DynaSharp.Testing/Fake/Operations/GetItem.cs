using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>GetItem</c>: the item of a key, <c>{"Item":{...}}</c>, or <c>{}</c> when there is none. Every read sees the last write, with or without <c>ConsistentRead</c>.</summary>
internal static class GetItem
{
    /// <summary>The operation.</summary>
    public const string Name = "GetItem";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, Name, "TableName", "Key", "ConsistentRead", "ProjectionExpression", "ExpressionAttributeNames", "ReturnConsumedCapacity");
        var tableName = TableOperations.ReadTableName(request);
        var key = RequestJson.ReadItem(RequestJson.Required(request, "Key"), "Key");
        if (RequestJson.Optional(request, "ConsistentRead") is { } consistentRead)
        {
            _ = RequestJson.ReadBool(consistentRead, "ConsistentRead");
        }

        ItemRequests.ThrowIfPresent(request, "ProjectionExpression", Name);
        ItemRequests.ThrowIfNotNone(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities, Name);
        ItemRequests.ThrowIfMapsWithoutExpression(request);
        var table = db.GetTable(tableName);
        if (table.Get(table.KeySchema.ReadKey(key)) is { } item)
        {
            ResponseJson.WriteItem(response, "Item", item);
        }
    }
}
