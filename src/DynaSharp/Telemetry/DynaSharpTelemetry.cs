using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DynaSharp.Telemetry;

/// <summary>
/// The process-wide activity source and meter, both named <c>DynaSharp</c>. Without a listener they cost a null check.
/// </summary>
internal static class DynaSharpTelemetry
{
    internal static readonly ActivitySource ActivitySource = new(TelemetryNames.Source);

    internal static readonly Meter Meter = new(TelemetryNames.Source);

    // Advisory bucket boundaries in seconds from the database client metric conventions.
    internal static readonly Histogram<double> OperationDuration = Meter.CreateHistogram(
        TelemetryNames.DurationMetric,
        unit: "s",
        description: "Duration of DynamoDB client operations.",
        tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10] });

    internal static readonly Counter<long> Retries = Meter.CreateCounter<long>(
        TelemetryNames.RetriesMetric,
        unit: "{retry}",
        description: "Number of times DynamoDB client operations sent a request again after a failed attempt.");

    internal static readonly Counter<long> Throttles = Meter.CreateCounter<long>(
        TelemetryNames.ThrottlesMetric,
        unit: "{throttle}",
        description: "Number of throttling errors DynamoDB returned to client operations, whether or not the attempt was retried.");

    internal static readonly Counter<long> ConflictRetries = Meter.CreateCounter<long>(
        TelemetryNames.ConflictRetriesMetric,
        unit: "{retry}",
        description: "Number of times RetryOnConflictAsync ran its delegate again after a conflict.");

    internal static readonly Counter<long> ConflictsExhausted = Meter.CreateCounter<long>(
        TelemetryNames.ConflictsExhaustedMetric,
        unit: "{conflict}",
        description: "Number of conflicts RetryOnConflictAsync passed on to the caller because the delegate had used up its runs.");

    internal static readonly Counter<long> SkippedItems = Meter.CreateCounter<long>(
        TelemetryNames.SkippedItemsMetric,
        unit: "{item}",
        description: "Number of items a read with OnMappingFailure skipped because their type could not read them.");

    internal static readonly UpDownCounter<long> StreamShards = Meter.CreateUpDownCounter<long>(
        TelemetryNames.StreamShardsMetric,
        unit: "{shard}",
        description: "Number of shards that ReadStreamAsync is reading.");

    internal static readonly Counter<long> StreamRecords = Meter.CreateCounter<long>(
        TelemetryNames.StreamRecordsMetric,
        unit: "{record}",
        description: "Number of records that ReadStreamAsync handed to its caller.");

    internal static readonly Counter<long> StreamRetries = Meter.CreateCounter<long>(
        TelemetryNames.StreamRetriesMetric,
        unit: "{retry}",
        description: "Number of times ReadStreamAsync read a shard again after a transient failure.");

    internal static readonly Counter<long> StreamGaps = Meter.CreateCounter<long>(
        TelemetryNames.StreamGapsMetric,
        unit: "{gap}",
        description: "Number of times a shard read from Latest was read from Latest again after its iterator expired before it handed out a record, which skips what was written in between.");

    internal static readonly Counter<double> ConsumedCapacity = Meter.CreateCounter<double>(
        TelemetryNames.ConsumedCapacityMetric,
        unit: "{capacity_unit}",
        description: "Read and write capacity units DynamoDB reported as consumed by client operations. Recorded only for requests that ask for ConsumedCapacity.");
}
