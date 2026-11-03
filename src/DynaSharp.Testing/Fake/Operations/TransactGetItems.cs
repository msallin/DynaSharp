using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>TransactGetItems</c>: up to 100 gets, each a strongly consistent read of one item, all at one moment. <c>Responses</c> has one element per get, in its order: <c>{"Item":{...}}</c>,
/// or <c>{}</c> for a key without an item. A projection takes only what it names, not even the key. One item twice is a <c>ValidationException</c>.
/// </summary>
/// <remarks>A transactional read costs twice a strongly consistent read of the item, as the Developer Guide says.</remarks>
internal static class TransactGetItems
{
    /// <summary>The operation.</summary>
    public const string Name = "TransactGetItems";

    /// <summary>The message of DynamoDB for an item a transaction names twice.</summary>
    public const string SameItemTwice = "Transaction request cannot include multiple operations on one item";

    /// <summary>The most actions of a transaction.</summary>
    public const int MaxActions = 100;

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, Name, "TransactItems", "ReturnConsumedCapacity");
        var elements = RequestJson.ReadArray(RequestJson.Required(request, "TransactItems"), "TransactItems");
        Batches.ThrowIfLength(elements.Count, "transactItems", MaxActions);
        var capacity = SingleWrite.Enum(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities);

        var gets = new List<(FakeTable Table, ItemKey Key, ParsedProjection? Projection)>(elements.Count);
        var seen = new HashSet<(string, ItemKey)>();
        foreach (var element in elements)
        {
            RequestJson.Members(element, "TransactItems of " + Name, "Get");
            var get = RequestJson.Required(element, "Get");
            RequestJson.Members(get, "Get of " + Name, "TableName", "Key", "ProjectionExpression", "ExpressionAttributeNames");
            var table = db.GetTable(TableOperations.ReadTableName(get));
            var key = table.KeySchema.ReadKey(RequestJson.ReadItem(RequestJson.Required(get, "Key"), "Key"));
            var projection = ExpressionMembers.ReadProjection(get);
            Batches.ThrowIfSeen(seen, table, key, SameItemTwice);
            gets.Add((table, key, projection));
        }

        var shares = new List<CapacityShare>(gets.Count);
        response.WriteStartArray("Responses"u8);
        foreach (var (table, key, projection) in gets)
        {
            var item = table.Get(key);
            response.WriteStartObject();
            if (item is not null)
            {
                ResponseJson.WriteItem(response, "Item", projection is null ? item : projection.Apply(item));
            }

            response.WriteEndObject();
            if (capacity != ReturnConsumedCapacity.None)
            {
                shares.Add(new CapacityShare(table.Name, 2 * ReadCapacity.Units(item is null ? 0 : ItemSize.Of(item), consistent: true), []));
            }
        }

        response.WriteEndArray();
        if (capacity != ReturnConsumedCapacity.None)
        {
            WriteCapacity.WriteAll(response, capacity, shares);
        }
    }
}
