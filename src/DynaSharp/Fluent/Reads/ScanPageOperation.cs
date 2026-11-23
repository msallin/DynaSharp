using System.Diagnostics;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>A page of <c>Scan</c> as a command runs it: a scan, a segment of a parallel scan and a read of some attributes alike.</summary>
/// <typeparam name="TValue">What an item gives: the item, the attribute, or a tuple of them.</typeparam>
/// <typeparam name="TReader">What reads an item.</typeparam>
internal readonly struct ScanPageOperation<TValue, TReader> : ICommandOperation<PageData<TValue>>
    where TReader : IItemReader<TValue>
{
    public static string Name => ScanOperation.Name;

    public static PageData<TValue> ReadResponse(ref DynamoDbReader reader, CommandState state) => PageResponse.Read<TValue, TReader>(ref reader, state);

    public static void SetActivityTags(Activity activity, CommandState state) => PageResponse.SetScanTags(activity, state);

    public static void SetResponseTags(Activity activity, PageData<TValue> response) => ReadTags.SetResponse(activity, response.Count, response.ScannedCount);
}
