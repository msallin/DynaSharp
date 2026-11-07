using System.Diagnostics;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary><c>GetItem</c>, read for one attribute. The telemetry is that of the read it comes from, and no <c>Item</c> in the response means that no item has the key.</summary>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
internal readonly struct GetSelectOperation<TValue, TConverter> : ICommandOperation<SelectResult<TValue>>
    where TConverter : IDynamoDbConverter<TValue>
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
            return SelectedItem<TValue, TConverter>.Read(ref reader, state);
        }
        catch (DynamoDbMappingException failure)
        {
            failure.Key = state.ReadKey();
            throw;
        }
    }
}
