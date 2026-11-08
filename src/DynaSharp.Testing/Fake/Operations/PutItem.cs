using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>PutItem</c>: under its condition, the item replaces the one with its key, and every index follows at once. Its keys, the keys of the indexes and its size are checked first,
/// so an item DynamoDB does not store is a <c>ValidationException</c> also where the condition fails. <c>ReturnValues</c> takes <c>NONE</c> and <c>ALL_OLD</c>.
/// </summary>
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
        ItemRequests.ThrowIfLegacyWithExpressions(request);
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
        RequestModel.CheckItemWrite(request, Name);
        var tableName = TableOperations.ReadItemTableName(request);
        var item = RequestJson.ReadItem(RequestJson.Required(request, "Item"), "Item", ItemLayer.Enveloped);
        var options = SingleWrite.Read(request, Name);
        var table = db.GetTable(tableName);
        var key = table.KeySchema.KeyOf(table.Check(item));
        options.ThrowIfConditionFails(table.Get(key));
        var old = table.Put(item);
        options.Answer(response, table, new WriteOutcome(key, old, table.Get(key)));
    }
}
