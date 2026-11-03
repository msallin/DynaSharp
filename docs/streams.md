# Streams

A stream is the change log of a table: each write to an item becomes a record, kept for 24 hours. `DynamoDbStreamsClient` reads it through the DynamoDB Streams API: `DescribeStreamAsync` lists the shards and `ReadShardAsync` reads one.

`orders`, `customers` and `shop` are the tables of [the shop](getting-started.md#the-shop).

This is the plain API, not a consumer. Nothing stores how far a reader got, and nothing shares the shards out among several workers, so a reader that restarts or runs twice is yours to coordinate. For a consumer in production, a Lambda function with an event source mapping on the stream does both, and so does the Kinesis Client Library with the DynamoDB Streams Kinesis Adapter.

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

Without options it reads the environment as `DynamoDbClient` does, with `AWS_ENDPOINT_URL_DYNAMODB_STREAMS` for the endpoint. A `ServiceUrl` applies to both clients, also one that the first client read from `AWS_ENDPOINT_URL` (an endpoint from `AWS_ENDPOINT_URL_DYNAMODB` stays with its client, and the streams client reads its own variable): an emulator serves both APIs on one address, and AWS needs no URL for either. The client is thread-safe, so create one and keep it.

## Reading a stream

A stream is split into shards, and a record is in the shard of the partition of its item. DynamoDB closes a shard from time to time, and when its partition splits, and its children go on from there and name it in `ParentShardId`. So the changes of one item come in order only if a parent is read to its end before its children.

`ReadShardAsync` reads one shard from a position to its end, as an `IAsyncEnumerable<ChangeRecord>`:

```csharp
var shard = new ReadShardRequest { StreamArn = streamArn, ShardId = shardId, ShardIteratorType = ShardIteratorType.TrimHorizon };
await foreach (var record in streams.ReadShardAsync(shard, ct))
{
    var image = record.DynamoDb!.NewImage;
    if (image is not null && image.Is<Order>())
    {
        Console.WriteLine($"{record.EventName} #{image.To<Order>().Number}");
    }
}
```

It takes the iterator and pages through `GetRecords`.

- **The end.** The loop ends when the shard is closed and every record of it has been returned. An open shard has no end, so the loop runs until `ct` is cancelled.
- **An idle shard.** An open shard answers an empty page when nothing changed, and the reader then waits `EmptyPageDelay`, a second by default, on the `TimeProvider` of the client, so that it does not poll an idle shard without pause: DynamoDB may throttle a shard that more than two processes read at once, see [Reading and processing a stream](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/Streams.html#Streams.Processing).
- **An expired iterator.** An iterator lives 15 minutes. When one expires because the loop took longer over a record, the reader takes a new one after the last record it returned, so nothing is skipped and nothing repeats.
- **Images.** An image is an `Item`: [`Is<T>()`](without-a-model.md#item) says whether it is of an entity, by its discriminator, and `To<T>()` reads it as one.

The reader of the sample, [`OrderFeed.cs`](https://github.com/msallin/DynaSharp/blob/main/samples/DynaSharp.Sample/OrderFeed.cs), starts at every shard whose parent is not listed, reads each with `ReadShardAsync`, then finds the children with a `ShardFilter` and reads those. It hands on the order before and after each change.

> [!WARNING]
> The sample reads its shards with `Task.WhenAll`, which reports the failure of one shard only once the others end, and an open shard never ends. A reader that runs for good watches each shard and cancels the others when one fails.

Which images a record has follows from the view and the kind of change. An insert has no old image and a delete no new one, and `KeysOnly` gives only `Keys`. A delete by time to live has `UserIdentity` set, to the service `dynamodb.amazonaws.com`.

`TrimHorizon` starts at the oldest record still kept, so a new reader sees up to a day of changes again. `Latest` starts after the newest. A change is in the stream once, but a reader that starts over sees it again, so handle records in a way that a repeat does no harm. The position to keep is on each record: to resume after a restart, store `record.DynamoDb.SequenceNumber` of the last record handled per shard, and start after it, here the stored `after`:

```csharp
var resume = new ReadShardRequest
{
    StreamArn = streamArn,
    ShardId = shardId,
    ShardIteratorType = ShardIteratorType.AfterSequenceNumber,
    SequenceNumber = after,
};
```

## Errors

Both errors are a `DynamoDbException`, and the client does not retry either, because the same request fails the same way.

- `ExpiredIteratorException`: an iterator is valid for 15 minutes. `ReadShardAsync` renews it, see [Reading a stream](#reading-a-stream). A loop of `GetRecordsAsync` of its own takes a new one with `AfterSequenceNumber` and the sequence number of the last record handled: `Latest` would skip what was written in between.
- `TrimmedDataAccessException`: the position is older than the 24 hours that records are kept, so the changes in between are gone. `ReadShardAsync` throws it, at the start or while reading. Start again at `TrimHorizon`, and if the gap matters, rebuild what the reader keeps from the table.

A shard that DynamoDB removed, 24 hours after it closed, gives `ResourceNotFoundException`. `LimitExceededException` is retried, and surfaces only when every attempt got it.

## Testing

`StubClient.CreateStreams` makes a client over a `StubHandler`, and `StubResponse` writes the answers of the stream from entities: `StreamDescription` with its `Shard`s, `ShardIterator`, and `Records` of `Inserted<T>`, `Modified<T>`, `Removed<T>` and, for raw attributes, `Change`. A closed shard has an `endingSequenceNumber`, and a page without a next iterator ends it. This runs `OrderFeed` of the sample:

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
