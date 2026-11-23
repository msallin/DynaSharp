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
        var item = new GetItemMember<TValue, TReader>(state);
        var envelope = GetEnvelope.Read(ref reader, ref item);
        state.ResponseCapacity = envelope.ConsumedCapacity;
        return new SelectResult<TValue>(item.Value!, envelope.Found, envelope.ConsumedCapacity);
    }

    public static void SetActivityTags(Activity activity, CommandState state) =>
        ReadTags.SetProjection(activity, state.Read.ConsistentRead, state.Expressions.Projection.Resolve());
}
