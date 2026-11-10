using System.Diagnostics;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary><c>GetItem</c>, read for some attributes. The telemetry is that of the read it comes from, and no <c>Item</c> in the response means that no item has the key.</summary>
/// <typeparam name="TValue">What the item gives: the attribute, or a tuple of them.</typeparam>
/// <typeparam name="TReader">What reads the item.</typeparam>
internal readonly struct GetSelectOperation<TValue, TReader> : ICommandOperation<SelectResult<TValue>>
    where TReader : IItemReader<TValue>
{
    public static string Name => GetItemOperation.Name;

    public static SelectResult<TValue> ReadResponse(ref DynamoDbReader reader, CommandState state)
    {
        var value = default(TValue);
        var found = false;
        ConsumedCapacity? capacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Item"u8))
            {
                reader.ReadStartItem();
                value = ReadItem(ref reader, state);
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

        state.ResponseCapacity = capacity;
        return new SelectResult<TValue>(value!, found, capacity);
    }

    public static void SetActivityTags(Activity activity, CommandState state) =>
        ReadTags.SetProjection(activity, state.Read.ConsistentRead, state.Expressions.Projection.Resolve());

    // The key of the item is the one of the request, which stays until the call ends.
    private static TValue ReadItem(ref DynamoDbReader reader, CommandState state)
    {
        try
        {
            return TReader.Read(ref reader, state);
        }
        catch (DynamoDbMappingException failure)
        {
            failure.Key = state.ReadKey();
            throw;
        }
    }
}
