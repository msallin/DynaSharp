using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>The response of <c>GetItem</c>, read in one place for both APIs: the request API reads <c>Item</c> as an <see cref="Item"/>, a command as what it returns.</summary>
/// <typeparam name="T">How the item is read.</typeparam>
/// <param name="Item">The item, or the default if there is none under the key.</param>
/// <param name="Found">Whether the response has an item.</param>
/// <param name="ConsumedCapacity">The consumed capacity, if requested.</param>
internal readonly record struct GetEnvelope<T>(T? Item, bool Found, ConsumedCapacity? ConsumedCapacity)
    where T : IDynamoDbDocument<T>
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <returns>The response.</returns>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    public static GetEnvelope<T> Read(ref DynamoDbReader reader)
    {
        var item = default(T);
        var found = false;
        ConsumedCapacity? capacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Item"u8))
            {
                reader.ReadStartItem();
                item = T.ReadAttributes(ref reader);
                found = true;
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

        return new GetEnvelope<T>(item, found, capacity);
    }
}
