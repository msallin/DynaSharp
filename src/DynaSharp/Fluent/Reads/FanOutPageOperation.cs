using System.Diagnostics;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>A page of <c>Query</c> for a fan-out: the same call with the same telemetry as <see cref="QueryPageOperation{T}"/>, which also keeps the keys of the items.</summary>
/// <typeparam name="T">How the items are read.</typeparam>
internal readonly struct FanOutPageOperation<T> : ICommandOperation<PageData<T>>
    where T : IDynamoDbDocument<T>
{
    public static string Name => QueryOperation.Name;

    public static PageData<T> ReadResponse(ref DynamoDbReader reader, CommandState state) => PageResponse.Read<T>(ref reader, state, state.ItemKeys);

    public static void SetActivityTags(Activity activity, CommandState state) => QueryPageOperation<T>.SetActivityTags(activity, state);

    public static void SetResponseTags(Activity activity, PageData<T> response) => QueryPageOperation<T>.SetResponseTags(activity, response);
}
