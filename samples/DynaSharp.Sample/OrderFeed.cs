using DynaSharp.Streams;

namespace DynaSharp.Sample;

/// <summary>
/// Reads the stream of the shop table and hands each change of an order on, the reader of the Streams page of the guide. It starts at every shard
/// whose parent is not listed, reads each to its end, then finds the children with a <see cref="ShardFilter"/> and reads those, so the changes of one
/// item come in order. It keeps no position across restarts and shares no shards with other workers: that is what a consumer in production adds.
/// </summary>
internal static class OrderFeed
{
    // onChange gets the order before and after the change: (null, order) for an insert, (order, null) for a delete.
    // Shards are read at the same time, so it is called from several threads.
    public static async Task ReadAsync(DynamoDbStreamsClient streams, string streamArn, Action<Order?, Order?> onChange, CancellationToken ct)
    {
        var shards = await ShardsAsync(streams, streamArn, null, ct);
        var listed = shards.Select(shard => shard.ShardId).ToHashSet();
        var roots = shards.Where(shard => shard.ParentShardId is null || !listed.Contains(shard.ParentShardId));
        await Task.WhenAll(roots.Select(shard => ReadTreeAsync(streams, streamArn, shard.ShardId!, onChange, ct)));
    }

    // TrimHorizon while no record of the shard has been handled, and after the last one handled from then on.
    public static async Task<string?> IteratorAsync(DynamoDbStreamsClient streams, string streamArn, string shardId, string? after, CancellationToken ct)
    {
        var request = new GetShardIteratorRequest
        {
            StreamArn = streamArn,
            ShardId = shardId,
            ShardIteratorType = after is null ? ShardIteratorType.TrimHorizon : ShardIteratorType.AfterSequenceNumber,
            SequenceNumber = after,
        };
        return (await streams.GetShardIteratorAsync(request, ct)).ShardIterator;
    }

    private static async Task ReadTreeAsync(DynamoDbStreamsClient streams, string streamArn, string shardId, Action<Order?, Order?> onChange, CancellationToken ct)
    {
        await ReadShardAsync(streams, streamArn, shardId, onChange, ct);
        var children = await ShardsAsync(streams, streamArn, new ShardFilter { Type = ShardFilterType.ChildShards, ShardId = shardId }, ct);
        await Task.WhenAll(children.Select(child => ReadTreeAsync(streams, streamArn, child.ShardId!, onChange, ct)));
    }

    private static async Task ReadShardAsync(DynamoDbStreamsClient streams, string streamArn, string shardId, Action<Order?, Order?> onChange, CancellationToken ct)
    {
        string? last = null;
        var iterator = await IteratorAsync(streams, streamArn, shardId, last, ct);
        while (iterator is not null)
        {
            GetRecordsResponse page;
            try
            {
                page = await streams.GetRecordsAsync(new GetRecordsRequest { ShardIterator = iterator }, ct);
            }
            catch (ExpiredIteratorException)
            {
                // An iterator lives 15 minutes. Latest would skip what was written in between, so go on after the last record handled.
                iterator = await IteratorAsync(streams, streamArn, shardId, last, ct);
                continue;
            }

            foreach (var record in page.Records)
            {
                Apply(record, onChange);
                last = record.DynamoDb!.SequenceNumber;
            }

            // An open shard always has a next iterator, also after a page without records. Only a closed shard read to its end has none.
            // A shard allows five calls per second, so an empty page waits before the next one.
            iterator = page.NextShardIterator;
            if (iterator is not null && page.Records.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    private static void Apply(ChangeRecord record, Action<Order?, Order?> onChange)
    {
        var change = record.DynamoDb!;

        // The table holds customers too, and To<Order>() refuses an item of another entity.
        if ((change.NewImage ?? change.OldImage)?.Is<Order>() != true)
        {
            return;
        }

        switch (record.EventName)
        {
            case OperationType.Insert:
                onChange(null, change.NewImage!.To<Order>());
                break;
            case OperationType.Modify:
                onChange(change.OldImage!.To<Order>(), change.NewImage!.To<Order>());
                break;
            case OperationType.Remove:
                onChange(change.OldImage!.To<Order>(), null);
                break;
        }
    }

    private static async Task<List<Shard>> ShardsAsync(DynamoDbStreamsClient streams, string streamArn, ShardFilter? filter, CancellationToken ct)
    {
        var shards = new List<Shard>();
        string? start = null;
        do
        {
            var page = (await streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = streamArn, ShardFilter = filter, ExclusiveStartShardId = start }, ct)).StreamDescription!;
            shards.AddRange(page.Shards);
            start = page.LastEvaluatedShardId;
        }
        while (start is not null);

        return shards;
    }
}
