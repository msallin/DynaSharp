using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The response of <c>GetItem</c>, read in one place for both APIs. <c>Item</c> goes to the reader the caller gives: the request API reads it as an <see cref="Item"/>, a command as what it
/// returns, a whole item or some attributes.
/// </summary>
/// <param name="Found">Whether the response has an item.</param>
/// <param name="ConsumedCapacity">The consumed capacity, if requested.</param>
internal readonly record struct GetEnvelope(bool Found, ConsumedCapacity? ConsumedCapacity)
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <typeparam name="TItem">The reader of <c>Item</c>.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="item">The reader of the item, which keeps it. It is not called for a response without the member, which means that no item has the key.</param>
    /// <returns>The members both APIs read alike.</returns>
    public static GetEnvelope Read<TItem>(ref DynamoDbReader reader, ref TItem item)
        where TItem : struct, IResponseMember
    {
        var found = false;
        ConsumedCapacity? capacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Item"u8))
            {
                item.Read(ref reader);
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

        return new GetEnvelope(found, capacity);
    }
}
