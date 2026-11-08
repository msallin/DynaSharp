using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// An <c>UpdateItem</c>: under its condition, the update expression on the item, or on its key for a new one, which the update creates. Without an update expression the item
/// stays as it is, and one that does not exist is created with its key alone, as on DynamoDB. <c>ReturnValues</c> takes all five values.
/// </summary>
internal static class UpdateItem
{
    /// <summary>The operation.</summary>
    public const string Name = "UpdateItem";

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
            "UpdateExpression",
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
        var current = table.Get(key);
        options.ThrowIfConditionFails(current);
        options.Answer(response, table, Apply(table, key, current, options.Update));
    }

    /// <summary>Applies an update to the item of a key and stores the result, for <c>UpdateItem</c> and the <c>Update</c> of a transaction.</summary>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="current">The item as it is, or <see langword="null"/>.</param>
    /// <param name="update">The update, or <see langword="null"/> for none.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: the update is not valid on the item, changes a key attribute, or makes an item DynamoDB does not store. Nothing changes then.</exception>
    public static WriteOutcome Apply(FakeTable table, ItemKey key, Item? current, ParsedUpdate? update)
    {
        ArgumentNullException.ThrowIfNull(table);
        var keyItem = table.KeySchema.ToItem(key);
        if (update is null)
        {
            table.Put(current ?? keyItem);
            return new WriteOutcome(key, current, table.Get(key));
        }

        var (newImage, updatedOld, updatedNew) = update.Apply(current, keyItem);
        var old = table.Put(newImage, byUpdate: true);
        return new WriteOutcome(key, old, table.Get(key), updatedOld, updatedNew);
    }
}
