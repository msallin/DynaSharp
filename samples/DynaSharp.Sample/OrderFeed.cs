using DynaSharp.Streams;

namespace DynaSharp.Sample;

/// <summary>
/// Reads the stream of the shop table and hands each change of an order on, the reader of the Streams page of the guide. It starts at every shard
/// whose parent is not listed, reads each to its end with <see cref="DynamoDbStreamsClient.ReadShardAsync"/>, then finds the children with a
/// <see cref="ShardFilter"/> and reads those, so the changes of one item come in order. It keeps no position across restarts and shares no shards
/// with other workers: that is what a consumer in production adds.
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

    // A shard ends once it is closed and read, and its children go on from there.
    private static async Task ReadTreeAsync(DynamoDbStreamsClient streams, string streamArn, string shardId, Action<Order?, Order?> onChange, CancellationToken ct)
    {
        var shard = new ReadShardRequest { StreamArn = streamArn, ShardId = shardId, ShardIteratorType = ShardIteratorType.TrimHorizon };
        await foreach (var record in streams.ReadShardAsync(shard, ct))
        {
            Apply(record, onChange);
        }

        var children = await ShardsAsync(streams, streamArn, new ShardFilter { Type = ShardFilterType.ChildShards, ShardId = shardId }, ct);
        await Task.WhenAll(children.Select(child => ReadTreeAsync(streams, streamArn, child.ShardId!, onChange, ct)));
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
