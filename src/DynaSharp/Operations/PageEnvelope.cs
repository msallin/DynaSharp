using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The response of <c>Query</c> and <c>Scan</c>, read in one place for both APIs. <c>Items</c> and <c>LastEvaluatedKey</c> go to the readers the caller gives: a command reads the items into
/// what it returns and only locates the key, which it sends back as it is, and the request API parses both.
/// </summary>
/// <param name="Count">The number of items in the page.</param>
/// <param name="ScannedCount">The number of items evaluated before filtering.</param>
/// <param name="ConsumedCapacity">The consumed capacity, if requested.</param>
internal readonly record struct PageEnvelope(int Count, int ScannedCount, ConsumedCapacity? ConsumedCapacity)
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <typeparam name="TItems">The reader of <c>Items</c>.</typeparam>
    /// <typeparam name="TKey">The reader of <c>LastEvaluatedKey</c>.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="items">The reader of the items, which keeps them. It is not called for a response without them.</param>
    /// <param name="lastEvaluatedKey">The reader of the key, which keeps it. It is not called on the last page.</param>
    /// <returns>The members both APIs read alike.</returns>
    public static PageEnvelope Read<TItems, TKey>(ref DynamoDbReader reader, ref TItems items, ref TKey lastEvaluatedKey)
        where TItems : struct, IResponseMember
        where TKey : struct, IResponseMember
    {
        var count = 0;
        var scannedCount = 0;
        ConsumedCapacity? capacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Items"u8))
            {
                items.Read(ref reader);
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
                lastEvaluatedKey.Read(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                capacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new PageEnvelope(count, scannedCount, capacity);
    }
}
