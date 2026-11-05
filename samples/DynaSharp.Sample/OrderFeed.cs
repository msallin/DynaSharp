using DynaSharp.Streams;

namespace DynaSharp.Sample;

/// <summary>
/// Reads the stream of the shop table and hands each change of an order on, the reader of the Streams page of the guide.
/// <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> reads every shard, a parent before its children, so the changes of one item come in order, and the
/// first failure of any shard that is not transient ends the loop. It keeps no position across restarts and shares no shards with other processes: that is what a consumer in
/// production adds.
/// </summary>
internal static class OrderFeed
{
    // onChange gets the order before and after the change: (null, order) for an insert, (order, null) for a delete.
    // The loop calls it for one record at a time, so it needs no lock.
    public static async Task ReadAsync(DynamoDbStreamsClient streams, string streamArn, Action<Order?, Order?> onChange, CancellationToken ct)
    {
        var stream = new ReadStreamRequest { StreamArn = streamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };
        await foreach (var change in streams.ReadStreamAsync(stream, ct))
        {
            // The table holds customers too: As<Order>() is null for a change of another entity, and types the images of an order.
            if (change.Record.As<Order>() is { } order)
            {
                onChange(order.Before, order.After);
            }
        }
    }
}
