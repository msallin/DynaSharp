using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The members that the responses of <c>Query</c> and <c>Scan</c> share, read in one place so the two operations do not parse
/// the same envelope twice.
/// </summary>
/// <param name="Items">The items of the page.</param>
/// <param name="Count">The number of items in the page.</param>
/// <param name="ScannedCount">The number of items evaluated before filtering.</param>
/// <param name="LastEvaluatedKey">The key to continue with, or <see langword="null"/> on the last page.</param>
/// <param name="ConsumedCapacity">The consumed capacity, if requested.</param>
internal readonly record struct ItemPage(
    IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> Items,
    int Count,
    int ScannedCount,
    IReadOnlyDictionary<string, AttributeValue>? LastEvaluatedKey,
    ConsumedCapacity? ConsumedCapacity)
{
    /// <summary>Reads the response object of a <c>Query</c> or <c>Scan</c>; properties that are not known are skipped.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <returns>The page.</returns>
    public static ItemPage Read(ref DynamoDbReader reader)
    {
        IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> items = [];
        var count = 0;
        var scannedCount = 0;
        IReadOnlyDictionary<string, AttributeValue>? lastEvaluatedKey = null;
        ConsumedCapacity? consumedCapacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Items"u8))
            {
                items = OperationJson.ReadItems(ref reader);
            }
            else if (reader.AttributeNameEquals("Count"u8))
            {
                count = OperationJson.ReadInt32(ref reader);
            }
            else if (reader.AttributeNameEquals("ScannedCount"u8))
            {
                scannedCount = OperationJson.ReadInt32(ref reader);
            }
            else if (reader.AttributeNameEquals("LastEvaluatedKey"u8))
            {
                lastEvaluatedKey = OperationJson.ReadLastEvaluatedKey(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                consumedCapacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new ItemPage(items, count, scannedCount, lastEvaluatedKey, consumedCapacity);
    }
}
