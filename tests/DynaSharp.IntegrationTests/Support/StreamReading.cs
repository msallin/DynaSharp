using DynaSharp.Streams;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// Reads a stream the way a test needs to: from the start of its shards until the expected records have arrived. An emulator has one shard. Real DynamoDB has one or
/// more per partition of the table, and a record is in the shard of its partition, which no test can know.
/// </summary>
internal static class StreamReading
{
    /// <summary>Finds the ARN of the stream of a table.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="table">The table.</param>
    /// <returns>The stream ARN.</returns>
    public static async Task<string> FindStreamArnAsync(DynamoDbStreamsClient streams, string table)
    {
        // The stream of a new table on real DynamoDB is listed some time after the table turned active.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        while (true)
        {
            var response = await streams.ListStreamsAsync(new ListStreamsRequest { TableName = table }, timeout.Token);
            if (response.Streams.Count > 0)
            {
                return response.Streams.Single().StreamArn!;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    /// <summary>Reads records from the start of all shards until <paramref name="count"/> records have arrived.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="streamArn">The stream.</param>
    /// <param name="count">How many records to wait for.</param>
    /// <returns>The records in the order they were delivered. Within a shard that is the order of the writes; a test that compares the order writes to one partition.</returns>
    /// <exception cref="OperationCanceledException">Fewer records arrived within a minute.</exception>
    /// <exception cref="InvalidOperationException">The shards ended with fewer records.</exception>
    public static async Task<IReadOnlyList<ChangeRecord>> ReadAsync(DynamoDbStreamsClient streams, string streamArn, int count)
    {
        var description = (await streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = streamArn })).StreamDescription!;
        var iterators = new List<string>();
        foreach (var shard in description.Shards)
        {
            iterators.Add((await streams.GetShardIteratorAsync(new GetShardIteratorRequest
            {
                StreamArn = streamArn,
                ShardId = shard.ShardId!,
                ShardIteratorType = ShardIteratorType.TrimHorizon,
            })).ShardIterator!);
        }

        return await ReadFromAsync(streams, iterators, count);
    }

    /// <summary>Takes an iterator of every shard at the starting sequence number of its range, as <c>ReadStreamAsync</c> takes one of a child.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="streamArn">The stream.</param>
    /// <returns>The iterators.</returns>
    public static async Task<IReadOnlyList<string>> IteratorsAtStartAsync(DynamoDbStreamsClient streams, string streamArn)
    {
        var description = (await streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = streamArn })).StreamDescription!;
        var iterators = new List<string>();
        foreach (var shard in description.Shards)
        {
            iterators.Add((await streams.GetShardIteratorAsync(new GetShardIteratorRequest
            {
                StreamArn = streamArn,
                ShardId = shard.ShardId!,
                ShardIteratorType = ShardIteratorType.AtSequenceNumber,
                SequenceNumber = shard.SequenceNumberRange!.StartingSequenceNumber,
            })).ShardIterator!);
        }

        return iterators;
    }

    /// <summary>Reads records from an iterator until <paramref name="count"/> have arrived.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="iterator">The shard iterator to start at.</param>
    /// <param name="count">How many records to wait for.</param>
    /// <returns>The records in the order they were delivered.</returns>
    /// <exception cref="OperationCanceledException">Fewer records arrived within a minute.</exception>
    /// <exception cref="InvalidOperationException">The shard ended with fewer records.</exception>
    public static Task<IReadOnlyList<ChangeRecord>> ReadFromAsync(DynamoDbStreamsClient streams, string iterator, int count) => ReadFromAsync(streams, [iterator], count);

    /// <summary>Reads records from iterators, one per shard, until <paramref name="count"/> have arrived.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="iterators">The shard iterators to start at.</param>
    /// <param name="count">How many records to wait for.</param>
    /// <returns>The records in the order they were delivered.</returns>
    /// <exception cref="OperationCanceledException">Fewer records arrived within a minute.</exception>
    /// <exception cref="InvalidOperationException">The shards ended with fewer records.</exception>
    public static async Task<IReadOnlyList<ChangeRecord>> ReadFromAsync(DynamoDbStreamsClient streams, IReadOnlyList<string> iterators, int count)
    {
        // Real DynamoDB delivers stream records some time after the write, and an empty page does not mean the end; the emulators
        // deliver at once, but the wait costs them nothing.
        var records = new List<ChangeRecord>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var open = new List<string>(iterators);
        while (records.Count < count && open.Count > 0)
        {
            var before = records.Count;
            for (var i = open.Count - 1; i >= 0; i--)
            {
                var page = await streams.GetRecordsAsync(new GetRecordsRequest { ShardIterator = open[i] }, timeout.Token);
                records.AddRange(page.Records);
                if (page.NextShardIterator is { } next)
                {
                    open[i] = next;
                }
                else
                {
                    open.RemoveAt(i);
                }
            }

            if (records.Count == before)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
        }

        return records.Count >= count ? records : throw new InvalidOperationException($"Expected {count} stream records, but the shards ended after {records.Count}.");
    }
}
