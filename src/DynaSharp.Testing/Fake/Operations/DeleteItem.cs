using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>DeleteItem</c>: under its condition, the item of the key goes, with its entries in every index. A key without an item is no error. <c>ReturnValues</c> takes <c>NONE</c>
/// and <c>ALL_OLD</c>.
/// </summary>
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
        ItemRequests.ThrowIfLegacyWithExpressions(request);
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
        RequestModel.CheckItemWrite(request, Name);
        var tableName = TableOperations.ReadItemTableName(request);
        var keyItem = RequestJson.ReadItem(RequestJson.Required(request, "Key"), "Key", ItemLayer.Enveloped);
        var options = SingleWrite.Read(request, Name);
        var table = db.GetTable(tableName);
        var key = table.KeySchema.ReadKey(keyItem);
        options.ThrowIfConditionFails(table.Get(key));
        options.Answer(response, table, new WriteOutcome(key, table.Remove(key), null));
    }
}
