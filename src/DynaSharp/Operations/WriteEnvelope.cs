using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The response of <c>PutItem</c>, <c>UpdateItem</c> and <c>DeleteItem</c>, read in one place for both APIs: the request API reads <c>Attributes</c> as an <see cref="Item"/>, a command as what it
/// returns.
/// </summary>
/// <typeparam name="T">How the attributes are read.</typeparam>
/// <param name="Attributes">The attributes selected by <c>ReturnValues</c>, or the default if the response has none.</param>
/// <param name="HasAttributes">Whether the response has attributes.</param>
/// <param name="ConsumedCapacity">The consumed capacity, if requested.</param>
/// <param name="ItemCollectionMetrics">The item collection metrics, if requested and available.</param>
internal readonly record struct WriteEnvelope<T>(T? Attributes, bool HasAttributes, ConsumedCapacity? ConsumedCapacity, ItemCollectionMetrics? ItemCollectionMetrics)
    where T : IDynamoDbDocument<T>
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <returns>The response.</returns>
    public static WriteEnvelope<T> Read(ref DynamoDbReader reader)
    {
        var attributes = default(T);
        var hasAttributes = false;
        ConsumedCapacity? capacity = null;
        ItemCollectionMetrics? metrics = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Attributes"u8))
            {
                reader.ReadStartItem();
                attributes = T.ReadAttributes(ref reader);
                hasAttributes = true;
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                capacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else if (reader.AttributeNameEquals("ItemCollectionMetrics"u8))
            {
                metrics = OperationJson.ReadItemCollectionMetrics(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new WriteEnvelope<T>(attributes, hasAttributes, capacity, metrics);
    }
}
