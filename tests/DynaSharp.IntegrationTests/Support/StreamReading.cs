using DynaSharp.Streams;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>Reads a stream the way a test needs to: from the start of its first shard until the expected records have arrived.</summary>
internal static class StreamReading
{
    /// <summary>Finds the ARN of the stream of a table.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="table">The table.</param>
    /// <returns>The stream ARN.</returns>
    public static async Task<string> FindStreamArnAsync(DynamoDbStreamsClient streams, string table)
    {
        var response = await streams.ListStreamsAsync(new ListStreamsRequest { TableName = table });
        return response.Streams.Single().StreamArn;
    }

    /// <summary>Reads records from the start of the first shard until <paramref name="count"/> records have arrived.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="streamArn">The stream.</param>
    /// <param name="count">How many records to wait for.</param>
    /// <returns>The records in the order they were delivered.</returns>
    /// <exception cref="OperationCanceledException">Fewer records arrived within ten seconds.</exception>
    /// <exception cref="InvalidOperationException">The shard ended with fewer records.</exception>
    public static async Task<IReadOnlyList<ChangeRecord>> ReadAsync(DynamoDbStreamsClient streams, string streamArn, int count)
    {
        var description = (await streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = streamArn })).StreamDescription;
        var shard = description.Shards[0];
        var iterator = (await streams.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamArn = streamArn,
            ShardId = shard.ShardId,
            ShardIteratorType = ShardIteratorType.TrimHorizon,
        })).ShardIterator;

        return await ReadFromAsync(streams, iterator, count);
    }

    /// <summary>Reads records from an iterator until <paramref name="count"/> have arrived.</summary>
    /// <param name="streams">The streams client.</param>
    /// <param name="iterator">The shard iterator to start at.</param>
    /// <param name="count">How many records to wait for.</param>
    /// <returns>The records in the order they were delivered.</returns>
    /// <exception cref="OperationCanceledException">Fewer records arrived within ten seconds.</exception>
    /// <exception cref="InvalidOperationException">The shard ended with fewer records.</exception>
    public static async Task<IReadOnlyList<ChangeRecord>> ReadFromAsync(DynamoDbStreamsClient streams, string iterator, int count)
    {
        // Real DynamoDB delivers stream records some time after the write, and an empty page does not mean the end; the emulators
        // deliver at once, but the wait costs them nothing.
        var records = new List<ChangeRecord>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var next = (string?)iterator;
        while (records.Count < count && next is not null)
        {
            var page = await streams.GetRecordsAsync(new GetRecordsRequest { ShardIterator = next }, timeout.Token);
            records.AddRange(page.Records);
            next = page.NextShardIterator;
            if (page.Records.Count == 0 && records.Count < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
        }

        return records.Count >= count ? records : throw new InvalidOperationException($"Expected {count} stream records, but the shard ended after {records.Count}.");
    }
}
