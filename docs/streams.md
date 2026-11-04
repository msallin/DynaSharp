# Streams

A stream is the change log of a table: each write to an item becomes a record, kept for 24 hours. `DynamoDbStreamsClient` reads it through the DynamoDB Streams API: `ReadStreamAsync` reads every shard of a stream, `ReadShardAsync` reads one, and `DescribeStreamAsync` lists them.

`orders`, `customers` and `shop` are the tables of [the shop](getting-started.md#the-shop).

This reads a stream in one process, and is no consumer. Nothing stores how far a reader got, and nothing shares the shards out among several processes, so a reader that restarts or runs twice is yours to coordinate. For a consumer in production, a Lambda function with an event source mapping on the stream does both, and so does the Kinesis Client Library with the DynamoDB Streams Kinesis Adapter.

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

`ReadStreamAsync` reads the whole stream in this process, as one `IAsyncEnumerable<ShardChangeRecord>`: each record with the `ShardId` it came from.

```csharp
var stream = new ReadStreamRequest { StreamArn = streamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };
await foreach (var change in streams.ReadStreamAsync(stream, ct))
{
    var image = change.Record.DynamoDb!.NewImage;
    if (image is not null && image.Is<Order>())
    {
        Console.WriteLine($"{change.Record.EventName} #{image.To<Order>().Number} from {change.ShardId}");
    }
}
```

It lists the shards with `DescribeStream`, reads them at the same time, and starts the children of a shard once the shard has been read to its end.

- **The start.** `TrimHorizon` reads every shard from its oldest record, closed or open, each parent before its children. `Latest` reads the shards that are open at the start from now on. A shard closed at the start has nothing to read from now on, but a split can be in progress: DynamoDB may list a child of it only after the start, and that child is read from its first record. So a closed shard without children yet, or whose children opened less than a minute ago by their ids, is watched for children as a shard read to its end is, see **Late children**. A child is read from its first record, see the next point. No other position is taken: to resume, see [Resuming one shard](#resuming-one-shard).
- **Children and trimmed records.** A record is kept 24 hours, so a child read late could have lost its first records. Every record of a shard is younger than the shard, and the id of a shard of DynamoDB tells when it opened (`shardId-00000001414562045508-2bac9cd2`, Unix milliseconds): a child that opened less than 23 hours ago has lost nothing yet and is read from its trim horizon. An older child, or one whose id tells no time, is read at the `StartingSequenceNumber` of its range, where DynamoDB answers `TrimmedDataAccessException` when records are gone, and the loop ends with it. Only a loop a day behind gets there. DynamoDB Local numbers the records of all its streams from one counter and answers so for such a child although nothing is gone.
- **The order.** The changes of one item come in their order, since a parent comes before its children. Records of different shards come in no order at all.
- **The end.** The loop ends when no shard is left to read, which is when the stream was switched off and every shard has been read. While the stream is on it runs until `ct` is cancelled.
- **Late children.** DynamoDB can list a child a moment after its parent ended, and the Developer Guide says that a shard can split into several, without a number. So each child starts as soon as it is listed, and the reader of the parent keeps asking with `DescribeStream`: until a first child is listed, every `EmptyPageDelay` but at least every second; after it, every five seconds for one more minute, at most twelve calls. Asking ends when the stream is no longer on, since a stream switched off opens no shards. A child listed on a later look is logged, see [Observability](observability.md#logs).
- **The pace.** Records go through a small buffer, and a shard waits while the loop is behind: a slow loop slows every shard down, and nothing piles up in memory.
- **A failure.** The first exception of any shard ends the loop with that exception, and the other shards are stopped and awaited before it surfaces. Nothing is skipped: `TrimmedDataAccessException` surfaces too, see [Errors](#errors).
- **Stopping.** Leaving the loop, or cancelling `ct`, stops every shard and waits for it, so nothing runs on.
- **Telemetry.** Each shard is a span of its own under the span of the loop, with the calls of the shard under it and a link to its parent shard, and two metrics count the shards being read and the records handed out, see [Observability](observability.md#reading-a-stream).
- **Images.** An image is an `Item`: [`Is<T>()`](without-a-model.md#item) says whether it is of an entity, by its discriminator, and `To<T>()` reads it as one.

What it leaves out, on purpose: positions to resume at, a store of checkpoints, leases, several processes, a retry policy per shard, a limit per call, the size of the buffer, and typed records.

The reader of the sample, [`OrderFeed.cs`](https://github.com/msallin/DynaSharp/blob/main/samples/DynaSharp.Sample/OrderFeed.cs), is that loop, and hands on the order before and after each change.

Which images a record has follows from the view and the kind of change. An insert has no old image and a delete no new one, and `KeysOnly` gives only `Keys`. A delete by time to live has `UserIdentity` set, to the service `dynamodb.amazonaws.com`.

### Reading one shard

`ReadShardAsync` reads one shard from a position to its end, as an `IAsyncEnumerable<ChangeRecord>`. `ReadStreamAsync` reads each shard with it, and it is there for a reader that picks its shards itself:

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

### Resuming one shard

`TrimHorizon` starts at the oldest record still kept, so a new reader sees up to a day of changes again. `Latest` starts after the newest. A change is in the stream once, but a reader that starts over sees it again, so handle records in a way that a repeat does no harm. The position to keep is on each record: to resume after a restart, store the `ShardId` and `record.DynamoDb.SequenceNumber` of the last record handled per shard, and read that shard on after it, here the stored `after`:

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

- `ExpiredIteratorException`: an iterator is valid for 15 minutes. `ReadShardAsync` renews it, and so `ReadStreamAsync` does, see [Reading one shard](#reading-one-shard). A loop of `GetRecordsAsync` of its own takes a new one with `AfterSequenceNumber` and the sequence number of the last record handled: `Latest` would skip what was written in between.
- `TrimmedDataAccessException`: the position is older than the 24 hours that records are kept, so the changes in between are gone. `ReadShardAsync` and `ReadStreamAsync` throw it, at the start or while reading. Start again at `TrimHorizon`, and if the gap matters, rebuild what the reader keeps from the table.

A shard that DynamoDB removed, 24 hours after it closed, gives `ResourceNotFoundException`. `ReadStreamAsync` throws `TrimmedDataAccessException` for it instead when the shard was closed when listed: its records are gone, as those before a trimmed position are. `LimitExceededException` is retried, and surfaces only when every attempt got it.

## Testing

`StubClient.CreateStreams` makes a client over a `StubHandler`, and `StubResponse` writes the answers of the stream from entities: `StreamDescription` with its `Shard`s, `ShardIterator`, and `Records` of `Inserted<T>`, `Modified<T>`, `Removed<T>` and, for raw attributes, `Change`. A closed shard has an `endingSequenceNumber`, and a page without a next iterator ends it. This runs `OrderFeed` of the sample:

```csharp
using var stub = new StubHandler();
using var streams = StubClient.CreateStreams(stub);
stub.Enqueue(StubResponse.StreamDescription("Shop", StubResponse.Shard("shard-1", endingSequenceNumber: "200")));
stub.Enqueue(StubResponse.ShardIterator("iterator-1"));
stub.Enqueue(StubResponse.Records("iterator-2", StubResponse.Inserted(order)));
stub.Enqueue(StubResponse.Records(null, StubResponse.Modified(order, order with { Total = 120m }), StubResponse.Removed(order with { Total = 120m })));
stub.Enqueue(StubResponse.StreamDescription(new StreamDescription { StreamStatus = StreamStatus.Disabled }));

var seen = new List<(Order? Before, Order? After)>();
await OrderFeed.ReadAsync(streams, "arn:aws:dynamodb:us-east-1:123456789012:table/Shop/stream/2026-01-01T00:00:00.000", (before, after) => seen.Add((before, after)), ct);

stub.ThrowIfResponsesLeft();
Console.WriteLine(seen.Count); // 3
```

The last answer is the `DescribeStream` that looks for children of the closed shard: it finds none, and the stream is switched off, so the reading ends. With one shard the answers come in this order. Several shards are read at the same time, so a test of those answers each shard by what it asks for, with the responder of `StubHandler`. [Testing](testing.md) has the rest of the stub.
