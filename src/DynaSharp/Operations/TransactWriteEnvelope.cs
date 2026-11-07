using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>The response of <c>TransactWriteItems</c>, read in one place for both APIs, which read all of it alike.</summary>
/// <param name="ConsumedCapacity">The consumed capacity per table, empty if not requested.</param>
/// <param name="ItemCollectionMetrics">The item collection metrics per table, or <see langword="null"/> if the response has none.</param>
internal readonly record struct TransactWriteEnvelope(IReadOnlyList<ConsumedCapacity> ConsumedCapacity, Dictionary<string, IReadOnlyList<ItemCollectionMetrics>>? ItemCollectionMetrics)
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <returns>The response.</returns>
    public static TransactWriteEnvelope Read(ref DynamoDbReader reader)
    {
        IReadOnlyList<ConsumedCapacity> capacity = [];
        Dictionary<string, IReadOnlyList<ItemCollectionMetrics>>? metrics = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("ConsumedCapacity"u8))
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

        return new TransactWriteEnvelope(capacity, metrics);
    }
}
