using System.Diagnostics;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>A page of <c>Scan</c>, read for one attribute: the same call with the same telemetry as <see cref="ScanPageOperation{T}"/>.</summary>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
internal readonly struct ScanSelectPageOperation<TValue, TConverter> : ICommandOperation<PageData<TValue>>
    where TConverter : IDynamoDbConverter<TValue>
{
    public static string Name => ScanOperation.Name;

    public static PageData<TValue> ReadResponse(ref DynamoDbReader reader, CommandState state) => PageResponse.Read<TValue, SelectedItem<TValue, TConverter>>(ref reader, state);

    public static void SetActivityTags(Activity activity, CommandState state) => PageResponse.SetScanTags(activity, state);

    public static void SetResponseTags(Activity activity, PageData<TValue> response) => ReadTags.SetResponse(activity, response.Count, response.ScannedCount);
}
