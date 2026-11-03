using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>BatchGetItem</c>: up to 100 keys over several tables, each read as <c>GetItem</c> reads it, with the projection of its table. <c>Responses</c> holds a list for every table
/// of the request, empty where no key has an item, and <c>UnprocessedKeys</c> is always empty; a test of what a client does with unprocessed keys queues that answer on the handler.
/// </summary>
/// <remarks>The capacity of a batch rounds each item up to 4 KB on its own, as the Developer Guide says, and a key without an item costs what a read of nothing costs.</remarks>
internal static class BatchGetItem
{
    /// <summary>The operation.</summary>
    public const string Name = "BatchGetItem";

    private const int MaxKeys = 100;

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, Name, "RequestItems", "ReturnConsumedCapacity");
        var tables = RequestJson.ReadObject(RequestJson.Required(request, "RequestItems"), "RequestItems");
        Batches.ThrowIfLength(tables.Count, "requestItems", MaxKeys);
        var capacity = SingleWrite.Enum(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities);
        var reads = Read(db, tables);

        var shares = new List<CapacityShare>();
        response.WriteStartObject("Responses"u8);
        foreach (var (table, keys, consistent, projection) in reads)
        {
            var found = new List<Item>(keys.Count);
            foreach (var key in keys)
            {
                var item = table.Get(key);
                if (item is not null)
                {
                    found.Add(projection is null ? item : projection.Apply(item));
                }

                if (capacity != ReturnConsumedCapacity.None)
                {
                    shares.Add(new CapacityShare(table.Name, ReadCapacity.Units(item is null ? 0 : ItemSize.Of(item), consistent), []));
                }
            }

            ResponseJson.WriteItems(response, table.Name, found);
        }

        response.WriteEndObject();
        response.WriteStartObject("UnprocessedKeys"u8);
        response.WriteEndObject();
        if (capacity != ReturnConsumedCapacity.None)
        {
            WriteCapacity.WriteAll(response, capacity, shares);
        }
    }

    // {"Forum":{"Keys":[{...},{...}],"ConsistentRead":true,"ProjectionExpression":"#n","ExpressionAttributeNames":{"#n":"Name"}}}
    private static List<(FakeTable Table, List<ItemKey> Keys, bool Consistent, ParsedProjection? Projection)> Read(FakeDatabase db, IReadOnlyList<JsonProperty> tables)
    {
        var reads = new List<(FakeTable, List<ItemKey>, bool, ParsedProjection?)>();
        var seen = new HashSet<(string, ItemKey)>();
        foreach (var entry in tables)
        {
            var where = "RequestItems of " + Name;
            RequestJson.Members(entry.Value, where, "Keys", "ConsistentRead", "ProjectionExpression", "ExpressionAttributeNames");
            var elements = RequestJson.ReadArray(RequestJson.Required(entry.Value, "Keys"), "Keys");
            Batches.ThrowIfLength(elements.Count, "keys", MaxKeys);
            if (seen.Count + elements.Count > MaxKeys)
            {
                throw FakeError.Validation($"Too many items requested for the {Name} call");
            }

            var consistent = RequestJson.Optional(entry.Value, "ConsistentRead") is { } value && RequestJson.ReadBool(value, "ConsistentRead");
            var projection = ExpressionMembers.ReadProjection(entry.Value);
            var table = db.GetTable(entry.Name);
            var keys = new List<ItemKey>(elements.Count);
            foreach (var element in elements)
            {
                var key = table.KeySchema.ReadKey(RequestJson.ReadItem(element, "Keys"));
                Batches.ThrowIfSeen(seen, table, key, "Provided list of item keys contains duplicates");
                keys.Add(key);
            }

            reads.Add((table, keys, consistent, projection));
        }

        return reads;
    }
}
