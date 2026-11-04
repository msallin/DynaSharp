using DynaSharp.Streams;

namespace DynaSharp.Sample;

/// <summary>
/// Reads the stream of the shop table and hands each change of an order on, the reader of the Streams page of the guide.
/// <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> reads every shard, a parent before its children, so the changes of one item come in order, and the
/// first failure of any shard ends the loop. It keeps no position across restarts and shares no shards with other processes: that is what a consumer in
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
            Apply(change.Record, onChange);
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
}
