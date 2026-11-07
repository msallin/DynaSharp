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
        GetEnvelope<T> envelope;
        try
        {
            envelope = GetEnvelope<T>.Read(ref reader);
        }
        catch (DynamoDbMappingException failure)
        {
            // The key of the item is the one of the request, which stays until the call ends.
            failure.Key = state.ReadKey();
            throw;
        }

        state.ResponseCapacity = envelope.ConsumedCapacity;
        return new GetResult<T>(envelope.Item, envelope.Found, envelope.ConsumedCapacity);
    }

    public static void SetActivityTags(Activity activity, CommandState state) =>
        ReadTags.SetProjection(activity, state.Read.ConsistentRead, state.Expressions.Projection.Resolve());
}
