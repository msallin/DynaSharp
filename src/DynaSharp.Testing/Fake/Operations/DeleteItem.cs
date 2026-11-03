using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>DeleteItem</c>: the item of the key goes, with its entries in every index. A key without an item is no error.</summary>
internal static class DeleteItem
{
    /// <summary>The operation.</summary>
    public const string Name = "DeleteItem";

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
            "Key",
            "ConditionExpression",
            "ExpressionAttributeNames",
            "ExpressionAttributeValues",
            "ReturnValues",
            "ReturnValuesOnConditionCheckFailure",
            "ReturnConsumedCapacity",
            "ReturnItemCollectionMetrics");
        var tableName = TableOperations.ReadTableName(request);
        var key = RequestJson.ReadItem(RequestJson.Required(request, "Key"), "Key");
        SingleWrite.ThrowIfUnsupported(request, Name);
        var table = db.GetTable(tableName);
        table.Remove(table.KeySchema.ReadKey(key));
    }
}
