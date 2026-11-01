using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The members that the responses of <c>PutItem</c>, <c>UpdateItem</c> and <c>DeleteItem</c> share, read in one place.
/// </summary>
/// <param name="Attributes">The attributes selected by <c>ReturnValues</c>.</param>
/// <param name="ConsumedCapacity">The consumed capacity, if requested.</param>
/// <param name="ItemCollectionMetrics">The item collection metrics, if requested and available.</param>
internal readonly record struct ItemWriteResult(
    IReadOnlyDictionary<string, AttributeValue>? Attributes,
    ConsumedCapacity? ConsumedCapacity,
    ItemCollectionMetrics? ItemCollectionMetrics)
{
    /// <summary>Reads the response object of a write; properties that are not known are skipped.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <returns>The result.</returns>
    public static ItemWriteResult Read(ref DynamoDbReader reader)
    {
        IReadOnlyDictionary<string, AttributeValue>? attributes = null;
        ConsumedCapacity? consumedCapacity = null;
        ItemCollectionMetrics? itemCollectionMetrics = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Attributes"u8))
            {
                attributes = AttributeValueConverter.ReadItem(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                consumedCapacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else if (reader.AttributeNameEquals("ItemCollectionMetrics"u8))
            {
                itemCollectionMetrics = OperationJson.ReadItemCollectionMetrics(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new ItemWriteResult(attributes, consumedCapacity, itemCollectionMetrics);
    }
}
