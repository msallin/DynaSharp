using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>GetItem</c>: the item of a key, <c>{"Item":{...}}</c>, or <c>{}</c> when there is none, with what <c>ProjectionExpression</c> takes of it and the <c>ConsumedCapacity</c> of
/// <see cref="ReadCapacity"/>. Every read sees the last write, with or without <c>ConsistentRead</c>.
/// </summary>
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
        ItemRequests.ThrowIfLegacyWithExpressions(request);
        RequestJson.Members(request, Name, "TableName", "Key", "ConsistentRead", "ProjectionExpression", "ExpressionAttributeNames", "ReturnConsumedCapacity");
        var tableName = TableOperations.ReadItemTableName(request);
        var key = RequestJson.ReadItem(RequestJson.Required(request, "Key"), "Key");
        var consistent = RequestJson.Optional(request, "ConsistentRead") is { } consistentRead && RequestJson.ReadBool(consistentRead, "ConsistentRead");
        var capacity = RequestJson.Optional(request, "ReturnConsumedCapacity") is { } capacityValue
            ? RequestJson.ReadEnum(capacityValue, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities, noneIsDefault: true)
            : ReturnConsumedCapacity.None;
        var projection = ReadProjection(request);
        var table = db.GetTable(tableName);
        var item = table.Get(table.KeySchema.ReadKey(key));
        if (item is not null)
        {
            ResponseJson.WriteItem(response, "Item", projection is null ? item : projection.Apply(item));
        }

        // The units are those of the whole item, whatever the projection takes of it, and those of an empty read for a key without an item.
        ReadCapacity.Write(response, capacity, table.Name, ReadCapacity.Units(item is null ? 0 : ItemSize.Of(item), consistent), index: null);
    }

    private static ParsedProjection? ReadProjection(JsonElement request)
    {
        if (RequestJson.Optional(request, "ProjectionExpression") is not { } text)
        {
            ItemRequests.ThrowIfMapsWithoutExpression(request);
            return null;
        }

        var names = RequestJson.Optional(request, "ExpressionAttributeNames") is { } namesValue ? RequestJson.ReadStringMap(namesValue, "ExpressionAttributeNames") : null;
        var scope = new PlaceholderScope(names, null);
        var projection = Expressions.ParseProjection(RequestJson.ReadString(text, "ProjectionExpression"), scope);
        scope.ThrowIfUnused();
        return projection;
    }
}
