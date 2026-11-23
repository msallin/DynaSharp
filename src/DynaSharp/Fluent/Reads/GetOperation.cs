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
        var item = new GetItemMember<T, DocumentItem<T>>(state);
        var envelope = GetEnvelope.Read(ref reader, ref item);
        state.ResponseCapacity = envelope.ConsumedCapacity;
        return new GetResult<T>(item.Value, envelope.Found, envelope.ConsumedCapacity);
    }

    public static void SetActivityTags(Activity activity, CommandState state) =>
        ReadTags.SetProjection(activity, state.Read.ConsistentRead, state.Expressions.Projection.Resolve());
}
