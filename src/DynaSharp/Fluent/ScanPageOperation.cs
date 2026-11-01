using System.Diagnostics;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>A page of <c>Scan</c> as a command runs it.</summary>
/// <typeparam name="T">How the items are read.</typeparam>
internal readonly struct ScanPageOperation<T> : ICommandOperation<PageData<T>>
    where T : IDynamoDbDocument<T>
{
    public static string Name => ScanOperation.Name;

    public static PageData<T> ReadResponse(ref DynamoDbReader reader, CommandState state) => PageResponse.Read<T>(ref reader, state);

    public static void SetActivityTags(Activity activity, CommandState state)
    {
        PageResponse.SetRequestTags(activity, state);
        if (state.TotalSegments > 0)
        {
            activity.SetTag(TelemetryNames.Segment, state.Segment);
            activity.SetTag(TelemetryNames.TotalSegments, state.TotalSegments);
        }
    }

    public static void SetResponseTags(Activity activity, PageData<T> response) => ReadTags.SetResponse(activity, response.Count, response.ScannedCount);
}
