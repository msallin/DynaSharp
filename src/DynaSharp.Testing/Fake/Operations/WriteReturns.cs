using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>What a write returns: <c>ReturnValues</c> and <c>ReturnItemCollectionMetrics</c>. <see cref="Check"/> runs before the write changes anything, <see cref="Write"/> after it.</summary>
/// <remarks>
/// <para>
/// <c>ALL_OLD</c> is the item before, <c>ALL_NEW</c> the item after, <c>UPDATED_OLD</c> and <c>UPDATED_NEW</c> the attributes an update changed, before and after. What is empty, such as
/// <c>ALL_OLD</c> of a new item, leaves <c>Attributes</c> out, as DynamoDB does.
/// </para>
/// <para>
/// Item collection metrics exist only for a table with a local secondary index: <c>{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0,1]}</c>. The fake does not
/// estimate sizes, so the range is always 0 to 1 GB, which is what DynamoDB reports for a collection far below its limit of 10 GB.
/// </para>
/// </remarks>
internal static class WriteReturns
{
    /// <summary>Checks what a write asks to return against what its operation allows.</summary>
    /// <param name="operation">The operation: <c>PutItem</c> and <c>DeleteItem</c> take <c>NONE</c> and <c>ALL_OLD</c>, <c>UpdateItem</c> all five.</param>
    /// <param name="returnValues">The <c>ReturnValues</c>.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a value the operation does not take.</exception>
    public static void Check(string operation, ReturnValue returnValues)
    {
        if (operation != UpdateItem.Name && returnValues is not (ReturnValue.None or ReturnValue.AllOld))
        {
            throw FakeError.Enveloped("ReturnValues can only be ALL_OLD or NONE");
        }
    }

    /// <summary>Writes <c>Attributes</c> and <c>ItemCollectionMetrics</c> into the response of a write that went through.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="table">The table.</param>
    /// <param name="returnValues">The <c>ReturnValues</c>.</param>
    /// <param name="metrics">The <c>ReturnItemCollectionMetrics</c>.</param>
    /// <param name="outcome">What the write did.</param>
    public static void Write(Utf8JsonWriter response, FakeTable table, ReturnValue returnValues, ReturnItemCollectionMetrics metrics, WriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(table);
        var attributes = returnValues switch
        {
            ReturnValue.None => null,
            ReturnValue.AllOld => outcome.OldImage,
            ReturnValue.AllNew => outcome.NewImage,
            ReturnValue.UpdatedOld => outcome.UpdatedOld,
            _ => outcome.UpdatedNew,
        };

        if (attributes is { Count: > 0 })
        {
            ResponseJson.WriteItem(response, "Attributes", attributes);
        }

        if (HasMetrics(table, metrics))
        {
            response.WritePropertyName("ItemCollectionMetrics"u8);
            WriteMetrics(response, table, outcome.Key);
        }
    }

    /// <summary>
    /// Writes <c>ItemCollectionMetrics</c> of a batch or a transaction, one list per table with a local secondary index, one entry per item written:
    /// <c>"ItemCollectionMetrics":{"Forum":[{"ItemCollectionKey":{...},"SizeEstimateRangeGB":[0,1]}]}</c>. Nothing when no table has one.
    /// </summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="metrics">The <c>ReturnItemCollectionMetrics</c>.</param>
    /// <param name="writes">The writes, with their tables.</param>
    public static void WriteAll(Utf8JsonWriter response, ReturnItemCollectionMetrics metrics, IEnumerable<(FakeTable Table, WriteOutcome Outcome)> writes)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(writes);
        var byTable = writes.Where(write => HasMetrics(write.Table, metrics)).GroupBy(static write => write.Table).ToList();
        if (byTable.Count == 0)
        {
            return;
        }

        response.WriteStartObject("ItemCollectionMetrics"u8);
        foreach (var table in byTable)
        {
            response.WriteStartArray(table.Key.Name);
            foreach (var (_, outcome) in table)
            {
                WriteMetrics(response, table.Key, outcome.Key);
            }

            response.WriteEndArray();
        }

        response.WriteEndObject();
    }

    private static bool HasMetrics(FakeTable table, ReturnItemCollectionMetrics metrics) => metrics == ReturnItemCollectionMetrics.Size && table.LocalIndexes.Count > 0;

    private static void WriteMetrics(Utf8JsonWriter response, FakeTable table, ItemKey key)
    {
        response.WriteStartObject();
        ResponseJson.WriteItem(response, "ItemCollectionKey", new Item(1) { [table.KeySchema.PartitionKeyName] = key.Partition });
        response.WriteStartArray("SizeEstimateRangeGB"u8);
        response.WriteNumberValue(0);
        response.WriteNumberValue(1);
        response.WriteEndArray();
        response.WriteEndObject();
    }
}
