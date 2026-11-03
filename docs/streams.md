# Streams

A stream is the change log of a table: each write to an item becomes a record, kept for 24 hours. `DynamoDbStreamsClient` reads it through the DynamoDB Streams API, with `DescribeStreamAsync`, `GetShardIteratorAsync` and `GetRecordsAsync`.

It is the plain API and no consumer, by scope. Nothing stores how far a reader got, and nothing shares the shards out among several workers, so a reader that restarts or runs twice is yours to coordinate. For a consumer in production, a Lambda function with an event source mapping on the stream does both, and so does the Kinesis Client Library with the DynamoDB Streams Kinesis Adapter.

## Turning a stream on

On a table that `CreateTable()` creates, `.Stream(...)` turns a stream on, and its view says what a record holds: `NewImage`, `OldImage`, `NewAndOldImages` or `KeysOnly`.

```csharp
var created = await orders.CreateTable()
    .Including<Customer>()
    .Stream(StreamViewType.NewAndOldImages)
    .IfNotExists()
    .WaitUntilActive()
    .SendAsync(ct);

string? streamArn = created.Table.LatestStreamArn;
```

`LatestStreamArn` of the table description names the stream, and the reader needs nothing else. It names the last stream the table had, also one that was switched off since, so `StreamSpecification` says whether it is on.

`IfNotExists()` leaves a table that exists as it is, its stream included. For that table, `UpdateTableAsync` of `DynaSharp.Requests` turns the stream on, and the wait after it gives the new ARN:

```csharp
var table = await shop.WaitUntilActiveAsync(ct);
if (table.StreamSpecification?.StreamEnabled != true)
{
    await client.UpdateTableAsync(new UpdateTableRequest
    {
        TableName = "Shop",
        StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages },
    }, ct);
    table = await shop.WaitUntilActiveAsync(ct);
}

var streamArn = table.LatestStreamArn!;
```

A table has one stream at a time. Another view takes turning the stream off and on again, which starts a new stream with a new ARN, and the records of the old one stay readable for 24 hours.

## Creating the client

Streams have their own endpoint, `https://streams.dynamodb.<region>.amazonaws.com`, which `AwsRegion.DynamoDbStreamsEndpoint` gives. `DynamoDbStreamsClient` takes the same `DynamoDbClientOptions` as `DynamoDbClient` and picks that endpoint itself, so the options of a client make the second one, which then shares its credentials:

```csharp
using DynaSharp;
using DynaSharp.Streams;

using var client = new DynamoDbClient();
using var streams = new DynamoDbStreamsClient(client.Options);
```

Without options it reads the environment as `DynamoDbClient` does, with `AWS_ENDPOINT_URL_DYNAMODB_STREAMS` for the endpoint. A `ServiceUrl` applies to both clients, also one that the first client read from the environment: an emulator serves both APIs on one address, and AWS needs no URL for either. The client is thread-safe, so create one and keep it.

## Reading a stream

A stream is split into shards, and a record is in the shard of the partition of its item. DynamoDB closes a shard from time to time, and when its partition splits, and its children go on from there and name it in `ParentShardId`. So the changes of one item come in order only if a parent is read to its end before its children.

The reader below starts at every shard whose parent is not listed, reads each to its end, then finds the children with a `ShardFilter` and reads those. An image is an `Item`, `Is<T>()` says whether it is of an entity, and `To<T>()` reads it as one.

```csharp
public static class OrderFeed
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
                iterator = await IteratorAsync(streams, streamArn, shardId, last, ct);
                continue;
            }

            foreach (var record in page.Records)
            {
                Apply(record, onChange);
                last = record.DynamoDb!.SequenceNumber;
            }

            // An open shard always has a next iterator, also after a page without records. Only a closed shard read to its end has none.
            iterator = page.NextShardIterator;
            if (iterator is not null && page.Records.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    private static async Task<string?> IteratorAsync(DynamoDbStreamsClient streams, string streamArn, string shardId, string? after, CancellationToken ct)
    {
        var response = await streams.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamArn = streamArn,
            ShardId = shardId,
            ShardIteratorType = after is null ? ShardIteratorType.TrimHorizon : ShardIteratorType.AfterSequenceNumber,
            SequenceNumber = after,
        }, ct);
        return response.ShardIterator;
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
```

`GetRecordsAsync` ends a shard only with a `NextShardIterator` of `null`, which comes once a closed shard has given all its records. An open shard answers with an empty page when nothing changed, or when DynamoDB looked at a part of the shard without finding a record, and with a next iterator every time. So an empty page is no end, and the reader waits a second before the next call: a shard allows five calls per second.

Which images a record has follows from the view and the kind of change. An insert has no old image and a delete no new one, and `KeysOnly` gives only `Keys`. A delete by time to live has `UserIdentity` set, to the service `dynamodb.amazonaws.com`.

`TrimHorizon` starts at the oldest record still kept, so a new reader sees up to a day of changes again. `Latest` starts after the newest. A change is in the stream once, but a reader that starts over sees it again, so handle records in a way that a repeat does no harm. To resume after a restart, store the `SequenceNumber` of the last record handled per shard, and start there with `AfterSequenceNumber`.

`Task.WhenAll` reports the failure of one shard only once the others end, which an open shard never does. A reader that runs for good watches each shard and cancels the others when one fails.

## Errors

Both errors are a `DynamoDbException`, and the client does not retry either, because the same request fails the same way.

- `ExpiredIteratorException`: an iterator is valid for 15 minutes, and a reader that took longer over a page, or paused, holds a dead one. Take a new one with `AfterSequenceNumber` and the sequence number of the last record handled, as the reader above does. `Latest` would skip what was written in between.
- `TrimmedDataAccessException`: the position is older than the 24 hours that records are kept, so the changes in between are gone. Start again at `TrimHorizon`, and if the gap matters, rebuild what the reader keeps from the table.

A shard that DynamoDB removed, 24 hours after it closed, gives `ResourceNotFoundException`. `LimitExceededException` is retried, and surfaces only when every attempt got it.

## Testing

`StubClient.CreateStreams` makes a client over a `StubHandler`, and `StubResponse` writes the answers of the stream from entities: `StreamDescription` with its `Shard`s, `ShardIterator`, and `Records` of `Inserted<T>`, `Modified<T>`, `Removed<T>` and, for raw attributes, `Change`. A closed shard has an `endingSequenceNumber`, and a page without a next iterator ends it:

```csharp
using var stub = new StubHandler();
using var streams = StubClient.CreateStreams(stub);
stub.Enqueue(StubResponse.StreamDescription("Shop", StubResponse.Shard("shard-1", endingSequenceNumber: "200")));
stub.Enqueue(StubResponse.ShardIterator("iterator-1"));
stub.Enqueue(StubResponse.Records("iterator-2", StubResponse.Inserted(order)));
stub.Enqueue(StubResponse.Records(null, StubResponse.Modified(order, order with { Total = 120m }), StubResponse.Removed(order with { Total = 120m })));
stub.Enqueue(StubResponse.StreamDescription("Shop"));

var seen = new List<(Order? Before, Order? After)>();
await OrderFeed.ReadAsync(streams, "arn:aws:dynamodb:us-east-1:123456789012:table/Shop/stream/2026-01-01T00:00:00.000", (before, after) => seen.Add((before, after)), ct);

stub.ThrowIfResponsesLeft();
Console.WriteLine(seen.Count); // 3
```

The last answer is the `DescribeStream` that looks for children of the closed shard and finds none. [Testing](testing.md) has the rest of the stub.
