using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>PutItem</c>: the item replaces the one with its key, after the checks of its keys, the keys of the indexes and its size, and every index follows at once.</summary>
internal static class PutItem
{
    /// <summary>The operation.</summary>
    public const string Name = "PutItem";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(
            request,
            Name,
            "TableName",
            "Item",
            "ConditionExpression",
            "ExpressionAttributeNames",
            "ExpressionAttributeValues",
            "ReturnValues",
            "ReturnValuesOnConditionCheckFailure",
            "ReturnConsumedCapacity",
            "ReturnItemCollectionMetrics");
        var tableName = TableOperations.ReadTableName(request);
        var item = RequestJson.ReadItem(RequestJson.Required(request, "Item"), "Item");
        SingleWrite.ThrowIfUnsupported(request, Name);
        db.GetTable(tableName).Put(item);
    }
}
