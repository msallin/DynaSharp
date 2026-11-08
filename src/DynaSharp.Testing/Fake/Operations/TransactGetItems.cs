using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>TransactGetItems</c>: up to 100 gets, each a strongly consistent read of one item, all at one moment. <c>Responses</c> has one element per get, in its order: <c>{"Item":{...}}</c>,
/// or <c>{}</c> for a key without an item. A projection takes only what it names, not even the key, and one that matches no attribute of the item gives <c>{}</c> as well.
/// One item twice is a <c>ValidationException</c>. A key that does not match the schema of its table cancels the transaction, with a <c>ValidationError</c> reason for that get and
/// <c>None</c> for the others, as <c>TransactWriteItems</c> does: both emulators answer <c>TransactionCanceledException</c> for it.
/// </summary>
/// <remarks>
/// A transactional read costs twice a strongly consistent read of the item, as the Developer Guide says. <c>INDEXES</c> repeats the units as <c>ReadCapacityUnits</c>, at the top and of
/// the table, as DynamoDB answers it.
/// </remarks>
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
        RequestModel.CheckTransaction(request, writes: false);
        var elements = RequestJson.ReadArray(RequestJson.Required(request, "TransactItems"), "TransactItems");
        var capacity = SingleWrite.Enum(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities);

        var gets = new List<(FakeTable Table, ItemKey Key, ParsedProjection? Projection)>(elements.Count);
        var seen = new HashSet<(string, ItemKey)>();
        var reasons = new CancellationReason[elements.Count];
        var cancelled = false;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            RequestJson.Members(element, "TransactItems of " + Name, "Get");
            var get = RequestJson.Required(element, "Get");
            RequestJson.Members(get, "Get of " + Name, "TableName", "Key", "ProjectionExpression", "ExpressionAttributeNames");
            var table = db.GetTable(TableOperations.ReadItemTableName(get));
            var keyMap = RequestJson.ReadItem(RequestJson.Required(get, "Key"), "Key", ItemLayer.Element);
            var projection = ExpressionMembers.ReadProjection(get);
            reasons[i] = new CancellationReason { Code = "None" };
            ItemKey key;
            try
            {
                key = table.KeySchema.ReadKey(keyMap);
            }
            catch (FakeError error) when (!error.IsInputError)
            {
                reasons[i] = new CancellationReason { Code = "ValidationError", Message = error.Message };
                cancelled = true;
                continue;
            }

            Batches.ThrowIfSeen(seen, table, key, SameItemTwice);
            gets.Add((table, key, projection));
        }

        if (cancelled)
        {
            throw FakeError.TransactionCanceled(reasons);
        }

        var shares = new List<CapacityShare>(gets.Count);
        response.WriteStartArray("Responses"u8);
        foreach (var (table, key, projection) in gets)
        {
            var item = table.Get(key);
            response.WriteStartObject();
            var projected = item is null || projection is null ? item : projection.Apply(item);
            if (projected is { Count: > 0 })
            {
                ResponseJson.WriteItem(response, "Item", projected);
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
            WriteCapacity.WriteAll(response, capacity, shares, WriteCapacity.Split.Read);
        }
    }
}
