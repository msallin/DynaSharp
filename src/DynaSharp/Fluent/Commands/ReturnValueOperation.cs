using System.Diagnostics;
using DynaSharp.Operations;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>
/// <c>UpdateItem</c> of one attribute as <see cref="ReturnCommand{TValue, TConverter}.TrySendAsync"/> runs it: the response of <see cref="UpdateValueOperation{TValue, TConverter}"/>, and a
/// failed condition as an answer.
/// </summary>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
internal readonly struct ReturnValueOperation<TValue, TConverter> : ICommandOperation<ReturnResult<TValue>>
    where TConverter : IDynamoDbConverter<TValue>
{
    public static string Name => UpdateItemOperation.Name;

    public static ReturnResult<TValue> ReadResponse(ref DynamoDbReader reader, CommandState state) => new(UpdateValueOperation<TValue, TConverter>.ReadResponse(ref reader, state));

    // The item that ReturnOldOnFailure() may have asked for is read as an Item, which cannot fail, and is not part of the answer.
    public static bool TryReadFailure(ReadOnlySpan<byte> utf8Json, CommandState state, out ReturnResult<TValue> response)
    {
        response = ReturnResult<TValue>.Failed;
        return WriteResponse.TryReadFailure<Item>(utf8Json, state, out _);
    }

    public static void SetResponseTags(Activity activity, ReturnResult<TValue> response)
    {
        if (!response.ConditionMet)
        {
            activity.SetTag(TelemetryNames.ConditionMet, false);
        }
    }
}
