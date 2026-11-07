using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The response of <c>BatchWriteItem</c>, read in one place for both APIs. <c>UnprocessedItems</c> goes to the reader the caller gives: a command locates it, because the next request sends it back
/// as it is, and the request API parses it.
/// </summary>
/// <param name="ConsumedCapacity">The consumed capacity per table, empty if not requested.</param>
/// <param name="ItemCollectionMetrics">The item collection metrics per table, or <see langword="null"/> if the response has none.</param>
internal readonly record struct BatchWriteEnvelope(IReadOnlyList<ConsumedCapacity> ConsumedCapacity, Dictionary<string, IReadOnlyList<ItemCollectionMetrics>>? ItemCollectionMetrics)
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <typeparam name="TUnprocessed">The reader of <c>UnprocessedItems</c>.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="unprocessedItems">The reader of what DynamoDB left, which keeps it. It is not called for a response without the member.</param>
    /// <returns>The members both APIs read alike.</returns>
    public static BatchWriteEnvelope Read<TUnprocessed>(ref DynamoDbReader reader, ref TUnprocessed unprocessedItems)
        where TUnprocessed : struct, IResponseMember
    {
        IReadOnlyList<ConsumedCapacity> capacity = [];
        Dictionary<string, IReadOnlyList<ItemCollectionMetrics>>? metrics = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("UnprocessedItems"u8))
            {
                unprocessedItems.Read(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                capacity = OperationJson.ReadConsumedCapacityList(ref reader);
            }
            else if (reader.AttributeNameEquals("ItemCollectionMetrics"u8))
            {
                metrics = OperationJson.ReadItemCollectionMetricsByTable(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new BatchWriteEnvelope(capacity, metrics);
    }
}
