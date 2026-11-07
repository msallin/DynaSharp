using System.Diagnostics;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary><c>GetItem</c> as a command runs it.</summary>
/// <typeparam name="T">How the item is read.</typeparam>
internal readonly struct GetOperation<T> : ICommandOperation<GetResult<T>>
    where T : IDynamoDbDocument<T>
{
    public static string Name => GetItemOperation.Name;

    public static GetResult<T> ReadResponse(ref DynamoDbReader reader, CommandState state)
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
                item = ReadItem(ref reader, state);
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
        return new GetResult<T>(item, found, capacity);
    }

    public static void SetActivityTags(Activity activity, CommandState state) =>
        ReadTags.SetProjection(activity, state.Read.ConsistentRead, state.Expressions.Projection.Resolve());

    // The key of the item is the one of the request, which stays until the call ends.
    private static T ReadItem(ref DynamoDbReader reader, CommandState state)
    {
        try
        {
            return T.ReadAttributes(ref reader);
        }
        catch (DynamoDbMappingException failure)
        {
            failure.Key = state.ReadKey();
            throw;
        }
    }
}
