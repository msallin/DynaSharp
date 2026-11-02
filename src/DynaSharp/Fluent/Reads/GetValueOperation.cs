using System.Diagnostics;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary><c>GetItem</c>, read for the item alone. It goes through the pipeline itself and not through <see cref="GetOperation{T}"/> and a wrapper, which would cost a state machine per call.</summary>
/// <typeparam name="T">How the item is read.</typeparam>
internal readonly struct GetValueOperation<T> : ICommandOperation<T?>
    where T : IDynamoDbDocument<T>
{
    public static string Name => GetItemOperation.Name;

    public static T? ReadResponse(ref DynamoDbReader reader, CommandState state) => GetOperation<T>.ReadResponse(ref reader, state).Item;

    public static void SetActivityTags(Activity activity, CommandState state) => GetOperation<T>.SetActivityTags(activity, state);
}
