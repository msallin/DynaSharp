using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>What the responses of <c>PutItem</c>, <c>UpdateItem</c> and <c>DeleteItem</c> share, read in one place.</summary>
internal static class WriteResponse
{
    /// <summary>Reads the response of a write.</summary>
    /// <typeparam name="T">How the returned attributes are read.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="state">The state of the command.</param>
    /// <returns>The result.</returns>
    public static WriteResult<T> Read<T>(ref DynamoDbReader reader, CommandState state)
        where T : IDynamoDbDocument<T>
    {
        var item = default(T);
        var hasItem = false;
        ConsumedCapacity? capacity = null;
        ItemCollectionMetrics? metrics = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Attributes"u8))
            {
                reader.ReadStartItem();
                item = T.ReadAttributes(ref reader);
                hasItem = true;
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

        state.ResponseCapacity = capacity;
        return new WriteResult<T>(item, hasItem, capacity, metrics, state.StoredVersion);
    }
}
