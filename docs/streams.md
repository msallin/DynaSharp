# Streams

A stream is the change log of a table: each write to an item becomes a record, kept for 24 hours. `DynamoDbStreamsClient` reads it through the DynamoDB Streams API: `ReadStreamAsync` reads every shard of a stream, of a table or by its ARN, `ReadShardAsync` reads one, and `DescribeStreamAsync` lists them. `ReadChangesAsync` of a table reads its changes as entities, and `As<T>()` types one record, see [Typed changes](#typed-changes).

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

`LatestStreamArn` of the table description names the stream, and a reader by ARN needs nothing else; [a reader of the table](#reading-the-stream-of-a-table) looks it up itself. It names the last stream the table had, also one that was switched off since, so `StreamSpecification` says whether it is on.

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

`ReadStreamAsync` reads the whole stream in this process, as one `IAsyncEnumerable<ShardChangeRecord>`: each record with the `ShardId` it came from and its `SequenceNumber`.

```csharp
var stream = new ReadStreamRequest { StreamArn = streamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };
await foreach (var change in streams.ReadStreamAsync(stream, ct))
{
    if (change.Record.As<Order>() is { } typed)
    {
        Console.WriteLine($"{typed.Kind} #{(typed.After ?? typed.Before)!.Number} from {change.ShardId} at {change.SequenceNumber}");
    }
}
```

It lists the shards with `DescribeStream`, reads them at the same time, and starts the children of a shard once the shard has been read to its end.

- **The start.** `TrimHorizon` reads every shard from its oldest record, closed or open, each parent before its children. `Latest` reads the shards that are open at the start from now on. A shard closed at the start has nothing to read from now on, but a split can be in progress: DynamoDB may list a child of it only after the start, and that child is read from its first record. So a closed shard without children yet, or whose children opened less than a minute ago by their ids, is watched for children as a shard read to its end is, see **Late children**. A child is read from its first record, see the next point. No other position is taken: to resume, see [Resuming one shard](#resuming-one-shard).
- **Children and trimmed records.** A record is kept 24 hours, so a child read late could have lost its first records. A child is read at the `StartingSequenceNumber` of its range: DynamoDB reads it from its first record there, and answers `TrimmedDataAccessException` when records are gone, and the loop ends with it. Only a loop a day behind gets there. DynamoDB Local numbers the records of all its streams from one counter and answers so for a child although nothing is gone, when records of other streams came between the start of the child and its first record.
- **The order.** The changes of one item come in their order, since a parent comes before its children. Records of different shards come in no order at all.
- **The end.** The loop ends when no shard is left to read, which is when the stream was switched off and every shard has been read. While the stream is on it runs until `ct` is cancelled, also on a stream just switched on that lists no shard yet: it is listed again every `EmptyPageDelay`, but at least every second, and its first shards are read from their first record.
- **Late children.** DynamoDB can list a child a moment after its parent ended, and the Developer Guide says that a shard can split into several, without a number. So each child starts as soon as it is listed, and the reader of the parent keeps asking with `DescribeStream`: until a first child is listed, every `EmptyPageDelay` but at least every second; after it, every five seconds for one more minute, at most twelve calls. Asking ends when the stream is no longer on, since a stream switched off opens no shards. A child listed on a later look is logged, see [Observability](observability.md#logs).
- **The pace.** Records go through a small buffer, and a shard waits while the loop is behind: a slow loop slows every shard down, and nothing piles up in memory.
- **A failure.** A shard that fails for a moment is read on from where it stopped, see [When a shard fails](#when-a-shard-fails). Any other exception of a shard ends the loop with that exception, and the other shards are stopped and awaited before it surfaces. Nothing is skipped without an exception, `TrimmedDataAccessException` surfaces too, see [Errors](#errors), but for an outage on a shard read from `Latest` that has handed out no record yet, see [When a shard fails](#when-a-shard-fails).
- **Stopping.** Leaving the loop, or cancelling `ct`, stops every shard and waits for it, so nothing runs on.
- **Telemetry.** Each shard is a span of its own under the span of the loop, with the calls of the shard under it and a link to its parent shard, and two metrics count the shards being read and the records handed out, see [Observability](observability.md#reading-a-stream).
- **Images.** An image is an `Item`. `change.Record.As<Order>()` reads the images of an order as a `Change<Order>`, and is null for another entity, see [Typed changes](#typed-changes). For a table without a model, [`Is<T>()`](without-a-model.md#item) and `To<T>()` take one image apart, and the getters of `Item` read it.

What it leaves out, on purpose: positions to resume at, a store of checkpoints, leases, several processes, settings of the retries of a shard, a limit per call, and the size of the buffer.

The reader of the sample, [`OrderFeed.cs`](https://github.com/msallin/DynaSharp/blob/main/samples/DynaSharp.Sample/OrderFeed.cs), is that loop, and hands on the order before and after each change.

Which images a record has follows from the view and the kind of change. An insert has no old image and a delete no new one, and `KeysOnly` gives only `Keys`. A delete by time to live has `UserIdentity` set, to the service `dynamodb.amazonaws.com`.

### Reading the stream of a table

`ReadStreamAsync` on a table takes the start and the streams client, and finds the stream itself: each loop over it starts with `DescribeTable` and reads the stream that `LatestStreamArn` names then.

```csharp
await foreach (var change in orders.ReadStreamAsync(streams, ShardIteratorType.Latest, cancellationToken: ct))
{
    Console.WriteLine($"{change.Record.EventName} in {change.ShardId}");
}
```

- **A new stream.** A stream that is switched off and on again is a new stream with a new ARN. A loop that starts after that reads the new one, so a reader that starts its loop again, after a failure for instance, follows the table. A loop that runs reads its stream until that ends.
- **No stream.** A table whose stream is off, or that never had one, throws `InvalidOperationException` when the loop starts, with the name of the table.
- **The view.** It is not checked: under `KeysOnly` a record has no image. `DescribeTableAsync()` of the table tells the view, in `StreamSpecification.StreamViewType`. [`ReadChangesAsync`](#typed-changes), the typed reading, checks it.
- **The clients.** The table describes itself through its own client, so the two clients go to the same account and region, as [the options of one client make the other](#creating-the-client). The reading takes `dynamodb:DescribeTable` besides the permissions of the stream.
- **The rest.** `emptyPageDelay` and `onJoined` are those of `ReadStreamRequest`, and everything else is as above.

### When a shard fails

A shard can fail for a moment: DynamoDB throttles it or answers a server error, the connection drops, an attempt times out. The client repeats such a call as it repeats any call, see [Errors and retries](errors.md#retries), and when its last attempt fails too, `ReadStreamAsync` does not end the loop. The reader of the shard waits and asks again from where it stopped, while every other shard reads on:

- **The position.** A page that failed is asked for again with the same iterator, which returns the same records, and a new iterator, when the old one expired in the meantime, starts after the last record the shard handed out, or at the start of the shard when it has handed out none. So no record comes twice and none is skipped, but for one case: a shard read from `Latest` that has handed out no record yet starts at `Latest` again, because DynamoDB Streams has no position by time, and what was written while its iterator was expired is skipped. It takes an outage of about the life of an iterator on such a shard, which is idle or new. Where that matters, read from `TrimHorizon`, which repeats what is still kept, or resume a shard from the sequence number of its last record, see [Resuming one shard](#resuming-one-shard). A position that DynamoDB has trimmed in the meantime throws `TrimmedDataAccessException`, as it does at any other time.
- **What counts.** What [`IsTransient`](errors.md#what-throws) says is transient, and a network failure or a timeout of an attempt. The first look for the shards of the stream is not repeated: nothing has been read then, and a setup that does not work, such as a wrong endpoint, shows at once.
- **The wait.** From a second up, doubling, to half a minute between two tries, at random within each, on the `TimeProvider` of the client.
- **How long.** Fifteen minutes from the failure that started it, the life of an iterator. Within them the reader resumes exactly while the iterator of the shard lives, and after the last record handed out once it expired; on a shard read from `Latest` that has handed out nothing yet an expired iterator skips, see the position above. A failure that outlasts them surfaces, and ends the loop as any other. A page that the shard returns in between starts the count again.
- **Stopping.** Cancelling or leaving the loop stops the wait at once.
- **Telemetry.** A warning when a shard starts to fail, once, and an information when it reads again, a counter of the retries, and the count of them on the span of the shard, see [Observability](observability.md#logs).

This takes no setting. A loop that wants every failure at once reads its shards with `ReadShardAsync`, which repeats nothing beyond the attempts of the client.

### Waiting until the reading holds its position

`Latest` reads what is written once each shard holds its position, which takes a call per shard after the loop starts. A test that writes and expects to read its write, or a view that loads the table and then follows its changes, waits for that with `OnJoined`:

```csharp
var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var live = new ReadStreamRequest
{
    StreamArn = streamArn,
    ShardIteratorType = ShardIteratorType.Latest,
    OnJoined = () => joined.TrySetResult(),
};
var reading = Task.Run(async () =>
{
    await foreach (var change in streams.ReadStreamAsync(live, ct))
    {
        Console.WriteLine($"{change.Record.EventName} in {change.ShardId}");
    }
}, ct);

await joined.Task.WaitAsync(ct);
// Load the table here: every write that DynamoDB acknowledges from now on comes through the loop.
```

It is called once, when every shard the loop starts with holds an iterator: under `Latest` the shards open at the start, under `TrimHorizon` those whose parent is not listed. A write that DynamoDB acknowledges after the call is read, unless the loop ends before: it lands in one of those shards behind their position, or in a shard that is read from its first record, a child or one listed later. A shard that fails for a moment delays it. It is not called when the loop fails or stops before, and a stream without a shard to start with calls it once it has been listed. It runs on the task of a shard, so keep it short; an exception from it ends the loop.

### Reading one shard

`ReadShardAsync` reads one shard from a position to its end, as an `IAsyncEnumerable<ChangeRecord>`. `ReadStreamAsync` reads each shard with it, and it is there for a reader that picks its shards itself:

```csharp
var shard = new ReadShardRequest { StreamArn = streamArn, ShardId = shardId, ShardIteratorType = ShardIteratorType.TrimHorizon };
await foreach (var record in streams.ReadShardAsync(shard, ct))
{
    if (record.As<Order>() is { } typed)
    {
        Console.WriteLine($"{typed.Kind} #{(typed.After ?? typed.Before)!.Number}");
    }
}
```

It takes the iterator and pages through `GetRecords`.

- **The end.** The loop ends when the shard is closed and every record of it has been returned. An open shard has no end, so the loop runs until `ct` is cancelled.
- **An idle shard.** An open shard answers an empty page when nothing changed, and the reader then waits `EmptyPageDelay`, a second by default, on the `TimeProvider` of the client, so that it does not poll an idle shard without pause: DynamoDB may throttle a shard that more than two processes read at once, see [Reading and processing a stream](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/Streams.html#Streams.Processing).
- **An expired iterator.** An iterator lives 15 minutes. When one expires because the loop took longer over a record, the reader takes a new one after the last record it returned, so nothing is skipped and nothing repeats.

### Resuming one shard

`TrimHorizon` starts at the oldest record still kept, so a new reader sees up to a day of changes again. `Latest` starts after the newest. A change is in the stream once, but a reader that starts over sees it again, so handle records in a way that a repeat does no harm. The position to keep is on each record: to resume after a restart, store the `ShardId` and the `SequenceNumber` of the last record handled per shard, which `ShardChangeRecord` has and `ChangeRecord` keeps in `DynamoDb.SequenceNumber`, and read that shard on after it, here the stored `after`:

```csharp
var resume = new ReadShardRequest
{
    StreamArn = streamArn,
    ShardId = shardId,
    ShardIteratorType = ShardIteratorType.AfterSequenceNumber,
    SequenceNumber = after,
};
```

## Typed changes

A table of a single-table design streams every entity it holds, and a stream cannot be filtered at its source: each reader gets every record. So the type of a change is decided where it is read, from its images, whose discriminator says what they are, as it does for a [mixed read](queries.md#several-types-in-one-query).

### One record

`As<T>()` of a `ChangeRecord` reads the images of a `T` into a `Change<T>`, and is null for a record of no `T`:

```csharp
var stream = new ReadStreamRequest { StreamArn = streamArn, ShardIteratorType = ShardIteratorType.TrimHorizon };
await foreach (var change in streams.ReadStreamAsync(stream, ct))
{
    if (change.Record.As<Order>() is { } placed)
    {
        Console.WriteLine($"order {placed.Kind}: {placed.Before?.Status} -> {placed.After?.Status}");
    }
    else if (change.Record.As<Customer>() is { } signedUp)
    {
        Console.WriteLine($"customer {signedUp.Kind}: {signedUp.After?.Name}");
    }
}
```

- **The change.** `Kind` is `Insert`, `Modify` or `Remove`, the `OperationType` of the record, and never `Unknown`. `Before` is the item read from the old image and `After` the one read from the new image, each null where there is no image of a `T`; for an entity that is a struct, the default value, and `Kind` tells. `Keys` and `SequenceNumber` are those of the record, `Id` the event id of the record or its sequence number where the event id is empty, as DynamoDB Local leaves it, so a key to tell a change seen again by, and `At` its `ApproximateCreationDateTime`, to the second. `Record` is the record itself, with `UserIdentity`, which tells a deletion by time to live. `ChangeRecord` has the same `Id` and `At`.
- **The type.** An image is a `T` when its discriminator attribute holds the discriminator of `T`, as `Is<T>()` tests. An entity without one, `[Discriminator(OptOut = true)]`, has nothing that tells its items from others, so every image is a `T`: that is for a table of one entity.
- **A type that changes.** An item can become another entity at the same key, an `ORDER` written over by a `CART`. That is one `MODIFY`, and two changes: the order removed, `Kind` `Remove` with `Before`, and the cart inserted, `Kind` `Insert` with `After`, both with the sequence number of the record. `As<Order>()` gives the first and `As<Cart>()` the second.
- **An image that does not fit.** An image that says it is a `T` and that `T` cannot read throws `DynamoDbMappingException`, with `Key` the keys of the record, as a read of the table does: it is a bug of the model or of a writer.

What can be typed depends on the view of the stream:

| View | What `As<T>()` gives |
|---|---|
| `NewAndOldImages` | Every change, a change of type included |
| `NewImage` | Inserts and modifications, without `Before`. A removal has no image, so it is null, and a change of type shows as a `Modify` of the new type |
| `OldImage` | Removals and modifications, without `After`. An insert has no image, so it is null |
| `KeysOnly` | Nothing: a record has no image |

### The changes of one entity

`ReadChangesAsync` of a typed table reads the stream of the table as [`ReadStreamAsync`](#reading-the-stream-of-a-table) does, and gives the changes of its entity, skipping every other one. In a single-table design, `shop.Of<Order>()` is the table of orders:

```csharp
await foreach (Change<Order> change in shop.Of<Order>().ReadChangesAsync(streams, ShardIteratorType.Latest, cancellationToken: ct))
{
    if (change.Kind == OperationType.Remove)
    {
        Console.WriteLine($"order #{change.Before!.Number} is gone");
    }
}
```

- **The view.** The reading needs `NewAndOldImages`, and checks it in the `DescribeTable` it starts with: another view throws `InvalidOperationException` with the name of the table and the view. Under `NewImage` a removal has no image to tell its type by, so a reader of orders would miss every deleted order without a word; under `OldImage` it would miss every new one. A table with another view reads its records with `ReadStreamAsync` and types them with `As<T>()`, knowing what the table above leaves out, or turns the stream off and on again with `NewAndOldImages`, which starts a new stream.
- **The rest.** The start, `emptyPageDelay`, `onJoined`, the stream that is read and every rule of the reading are those of `ReadStreamAsync`. An item whose type changed gives the removal of the old type, if it is the entity of the table, or the insert of the new one.

### Several entities

`ReadChangesAsync<T1, T2>` of a table, with up to four entities, gives a `MixedChange`: a change of one of them, a C# 15 union of the `Change<T>` of each type, as `Mixed` is of the items of a [mixed read](queries.md#unions-in-c-15). A `switch` names each type, and the compiler warns (`CS8509`) where adding a type leaves one unhandled:

```csharp
await foreach (var change in shop.ReadChangesAsync<Customer, Order>(streams, ShardIteratorType.Latest, cancellationToken: ct))
{
    Console.WriteLine(change switch
    {
        Change<Customer> customer => $"customer {customer.Kind}: {customer.After?.Email}",
        Change<Order> placed => $"order {placed.Kind}: {placed.After?.Total}",
    });
}
```

In C# 14, `Match` takes one function or action per type and is as exhaustive: `change.Match(customer => ..., placed => ...)`.

Every table has it, whatever type it was opened for, because the reading needs only the client, the name and the keys of the table. An application that opens the table once per entity reads from any of them, and the type of the table need not be one of the types read:

```csharp
await foreach (var change in orders.ReadChangesAsync<Customer, Order>(streams, ShardIteratorType.Latest, cancellationToken: ct))
{
    switch (change)
    {
        case Change<Customer> signedUp:
            Console.WriteLine($"customer {signedUp.Kind}");
            break;
    }
}
```

Without type arguments, `orders.ReadChangesAsync(streams, ...)` is the reading of the entity of the table, above.

The types are entities with a discriminator, in one attribute, and two types with the same discriminator throw `ArgumentException` at the call, as `Of<...>()` of a query does. An item that becomes another of the types at its key gives two changes, the removal first, and both come before the next record. Changes of entities that are not named are skipped. More than four entities, or a table without a model, read the records and take them apart with `As<T>()` or with `Item`.

Code that handles the changes is tested without a stream: a `Change<T>` converts to the `MixedChange` of its type, and `Change<T>` is a record that a test fills in. In C# 14 the constructor of each type does the same, `new MixedChange<Customer, Order>(change)`; the type of the argument picks it. The `default` value holds nothing and matches the `null` pattern; a reading never gives one.

```csharp
static string Describe(MixedChange<Customer, Order> change) => change switch
{
    Change<Customer> customer => $"customer {customer.After?.Email}",
    Change<Order> placed => $"order {placed.Kind} {placed.After?.Total}",
};

MixedChange<Customer, Order> inserted = new Change<Order> { Kind = OperationType.Insert, After = order, Record = new ChangeRecord() };
Console.WriteLine(Describe(inserted)); // order Insert, then the total of the order
```

### Skip changes that fail to map

An image that does not fit its type ends the reading with `DynamoDbMappingException`, as it ends a query. `onMappingFailure` skips such a change and hands it to the callback instead, as [`OnMappingFailure`](queries.md#skip-items-that-fail-to-map) of a query does: the exception, with `Key` the keys of the record, and the record with its shard, a `ShardChangeRecord`, so a log names the change by its shard and sequence number:

```csharp
await foreach (var change in orders.ReadChangesAsync(
    streams,
    ShardIteratorType.Latest,
    onMappingFailure: (failure, skipped) => Console.WriteLine($"skipped {skipped.Record.EventName} {skipped.SequenceNumber} of {skipped.ShardId}: {failure.AttributeName}"),
    cancellationToken: ct))
{
    Console.WriteLine(change.Kind);
}
```

The record holds the images, which are the values of the item, and `Key` holds its keys. Log the shard, the sequence number, the event name and the attribute that failed, not the record or the exception's `Key`, and the keys only where the application lets keys into its logs. The callback runs on the loop, before the next change is given, and what it throws ends the reading. A change of a type that changed is two changes, and only the one that fails is skipped. Each skipped change counts in `dynasharp.client.operation.skipped_items`, with the operation `GetRecords` and the table, see [Observability](observability.md#metrics).

## Errors

Both errors are a `DynamoDbException`, and the client does not retry either, because the same request fails the same way.

- `ExpiredIteratorException`: an iterator is valid for 15 minutes. `ReadShardAsync` renews it, and so `ReadStreamAsync` does, see [Reading one shard](#reading-one-shard). A loop of `GetRecordsAsync` of its own takes a new one with `AfterSequenceNumber` and the sequence number of the last record handled: `Latest` would skip what was written in between.
- `TrimmedDataAccessException`: the position is older than the 24 hours that records are kept, so the changes in between are gone. `ReadShardAsync` and `ReadStreamAsync` throw it, at the start or while reading. Start again at `TrimHorizon`, and if the gap matters, rebuild what the reader keeps from the table.

A shard that DynamoDB removed, 24 hours after it closed, gives `ResourceNotFoundException`. `ReadStreamAsync` throws `TrimmedDataAccessException` for it instead when the shard was closed when listed: its records are gone, as those before a trimmed position are. `LimitExceededException` is retried, and surfaces from `ReadShardAsync` only when every attempt got it; `ReadStreamAsync` then reads the shard again, see [When a shard fails](#when-a-shard-fails).

## Testing

`StubClient.CreateStreams` makes a client over a `StubHandler`, and `StubResponse` writes the answers of the stream from entities: `StreamDescription` with its `Shard`s, `ShardIterator`, and `Records` of `Inserted<T>`, `Modified<T>`, `Removed<T>` and, for raw attributes, `Change`. A closed shard has an `endingSequenceNumber`, and a page without a next iterator ends it. This runs `OrderFeed` of the sample:

```csharp
using var stub = new StubHandler();
using var streams = StubClient.CreateStreams(stub);
stub.Enqueue(StubResponse.StreamDescription("Shop", StreamStatus.Disabled, StubResponse.Shard("shard-1", endingSequenceNumber: "200")));
stub.Enqueue(StubResponse.ShardIterator("iterator-1"));
stub.Enqueue(StubResponse.Records("iterator-2", StubResponse.Inserted(order)));
stub.Enqueue(StubResponse.Records(null, StubResponse.Modified(order, order with { Total = 120m }), StubResponse.Removed(order with { Total = 120m })));
stub.Enqueue(StubResponse.StreamDescription("Shop", StreamStatus.Disabled));

var seen = new List<(Order? Before, Order? After)>();
await OrderFeed.ReadAsync(streams, "arn:aws:dynamodb:us-east-1:123456789012:table/Shop/stream/2026-01-01T00:00:00.000", (before, after) => seen.Add((before, after)), ct);

stub.ThrowIfResponsesLeft();
Console.WriteLine(seen.Count); // 3
```

The last answer is the `DescribeStream` that looks for children of the closed shard: it finds none, and the stream is switched off, so the reading ends. A stream that says `ENABLED` can still open shards, so the reader would look for children again and again: a script whose last shard closes says `DISABLED`. With one shard the answers come in this order. Several shards are read at the same time, so a test of those answers each shard by what it asks for, with the responder of `StubHandler`. [Testing](testing.md) has the rest of the stub.

A test that writes and reads takes `FakeDynamoDb`, whose tables record their changes in streams that `fake.CreateStreamsClient()` reads as DynamoDB's: `fake.EnableStream(table, viewType)` and `fake.DisableStream(table)` switch a stream, and a disable ends a reading. On that client an idle reader asks again at the next write instead of after `EmptyPageDelay`, so such a test takes milliseconds, see [Testing](testing.md#a-fake-dynamodb). That also means a write cuts the wait short, so a test that measures `EmptyPageDelay` itself, how much clock time passes before a reader sees a write, reads through a client of `StubClient.CreateStreams` over a script, which waits the whole delay.
