using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Time-bucketed partitions</see>: a stream of samples must not grow one partition without end, so time is cut into buckets, and a read of a range asks each bucket it touches and merges them.
/// The bucket size is a parameter: a smaller one spreads writes and costs more queries per read. A format in the key template would fix it at compile time, so here it is a number.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TimeBucketedPartitionsTests(Backend backend)
{
    private const string Cpu = "cpu";

    private static readonly DateTimeOffset Start = new(2026, 11, 1, 22, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(1)]
    [Arguments(6)]
    [Arguments(24)]
    public async Task ReadAsync_AnyBucketSize_ReadsTheRangeInOrderAcrossBuckets(int hoursPerBucket)
    {
        // Arrange (a sample every hour for a day and a half, which crosses midnight)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var samples = sandbox.Shop.Of<MetricSample>();
        var size = TimeSpan.FromHours(hoursPerBucket);
        await samples.BatchWrite().PutAll(Enumerable.Range(0, 36).Select(hour => new MetricSample { Metric = Cpu, Bucket = BucketOf(Start.AddHours(hour), size), At = Start.AddHours(hour), Value = hour })).SendAsync();

        // Act
        var read = await ReadAsync(samples, size, Start.AddHours(1), Start.AddHours(30));

        // Assert
        await Assert.That(read.Select(static sample => sample.Value).SequenceEqual(Enumerable.Range(1, 30))).IsTrue();
    }

    private static long BucketOf(DateTimeOffset at, TimeSpan size) => at.UtcTicks / size.Ticks;

    private static async Task<List<MetricSample>> ReadAsync(Table<MetricSample> samples, TimeSpan size, DateTimeOffset from, DateTimeOffset to)
    {
        var buckets = new List<PartitionKey<MetricSample>>();
        for (var bucket = BucketOf(from, size); bucket <= BucketOf(to, size); bucket++)
        {
            buckets.Add(MetricSample.Keys.Pk.Of(Cpu, bucket));
        }

        return await samples.Query(buckets, SortKey.Between(MetricSample.Keys.Sk.Format(from), MetricSample.Keys.Sk.Format(to))).ItemsAsync().ToListAsync();
    }
}

[DynamoDbEntity]
[Discriminator("SAMPLE")]
[PartitionKey("METRIC#{Metric}#{Bucket:d10}")]
[SortKey("{At}")]
internal sealed partial record MetricSample
{
    public required string Metric { get; init; }

    public required long Bucket { get; init; }

    public required DateTimeOffset At { get; init; }

    public required int Value { get; init; }
}
