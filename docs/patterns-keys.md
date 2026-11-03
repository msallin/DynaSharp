# Keys and partitions

A key is text, and DynamoDB keeps a partition in the byte order of its sort keys. These patterns put meaning into the keys: a hierarchy, a time, a shard. `shop` is the table of [Design patterns](patterns.md).

## Composite sort keys

One partition holds a hierarchy, such as the staff of a company by country and city, and a `begins_with` on a prefix reads it at any depth.

```csharp
[DynamoDbEntity]
[Discriminator("EMPLOYEE")]
[PartitionKey("ORG#{Org}")]
[SortKey("SITE#{Country}#{City}#EMP#{Number:d6}")]
public partial record Employee
{
    public required string Org { get; init; }
    public required string Country { get; init; }
    public required string City { get; init; }
    public required int Number { get; init; }
}
```

`BeginsWith` takes the leading fields, as many as the depth needs:

```csharp
var staff = shop.Of<Employee>();
var acme = Employee.Keys.Pk.Of("acme");

int everyone = await staff.Query(acme, Employee.Keys.Sk.BeginsWith()).CountAsync(ct);
int inSwitzerland = await staff.Query(acme, Employee.Keys.Sk.BeginsWith("CH")).CountAsync(ct);
var inZurich = staff.Query(acme, Employee.Keys.Sk.BeginsWith("CH", "Zurich")).ItemsAsync(ct);
```

Caveats:

- Order is the order of the text. `{Number:d6}` pads to six digits, so 10 sorts after 2, and padding orders only numbers that are not negative: `Keys.Of` throws `KeyFormatException` for -1. A signed value takes a format with a sign and a width, such as `{Delta:n4}`, which sorts -10 before -2.
- A time in a key is stored in UTC in one format, so two offsets of one instant sort by the instant. The formats are under [Defining an entity](entities.md#defining-an-entity).
- A value that holds a character below the delimiter, such as a space, sorts out of the order of the fields (`DYNA115`). `begins_with` on whole fields is not affected.

## Newest first with version-7 GUID sort keys

A sort key that is unique and in time order at once. A version-7 `Guid` starts with the time in milliseconds and ends with random bits, so two writes in one millisecond do not collide, and newest first is a backward read of the partition. DynaSharp stores a `Guid` as its lowercase text, `01a196fb-1600-7c3e-…`, in which the time comes first, so the text sorts by time. A `Guid.NewGuid()`, version 4, is random from its first bit and has no such order.

```csharp
[DynamoDbEntity]
[Discriminator("CHAT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("{Id}")]
public partial record ChatMessage
{
    public required string Room { get; init; }
    public required Guid Id { get; init; }
    public required string Text { get; init; }
}
```

```csharp
var messages = shop.Of<ChatMessage>();
await messages.Put(new ChatMessage { Room = "ann", Id = Guid.CreateVersion7(), Text = "Hello" }).SendAsync(ct);

var newest = await messages.Query(ChatMessage.Keys.Pk.Of("ann")).Descending().Limit(20).PageAsync(ct);
```

A range of time is a range of ids. `Guid.FirstVersion7(at)` and `Guid.LastVersion7(at)` are the lowest and the highest id of the millisecond of `at`, so `Between` of the two takes both milliseconds whole:

```csharp
var messages = shop.Of<ChatMessage>();
var to = DateTimeOffset.UtcNow;
var lastHour = ChatMessage.Keys.Sk.Between(Guid.FirstVersion7(to.AddHours(-1)), Guid.LastVersion7(to));
var recent = messages.Query(ChatMessage.Keys.Pk.Of("ann"), lastHour).ItemsAsync(ct);

var since = messages.Query(ChatMessage.Keys.Pk.Of("ann"), ChatMessage.Keys.Sk.Ge(Guid.FirstVersion7(to.AddDays(-1)))).ItemsAsync(ct);
```

Caveats:

- `Descending()` is `ScanIndexForward=false`. `Limit(n)` is per page, see [Queries and scans](queries.md#query).
- Within one millisecond the random part decides the order, also on one machine, and across machines the order is as good as their clocks.
- `Guid.CreateVersion7()` takes the clock of the machine, and `Guid.CreateVersion7(at)` a time from the caller, which a test or an import of old items sets.
- A service that already has ULIDs keeps them in a `string`: they sort the same way, and the bounds of a millisecond are its 10 characters of time followed by sixteen `0` or sixteen `Z`.

## Write sharding

A partition takes about 1000 writes a second, so a hot key, such as the votes of one poll, is spread over several partitions by a suffix. A random suffix spreads best, and a read then asks every shard. A suffix hashed from an id lets a read find its one shard.

```csharp
using System.Security.Cryptography;
using System.Text;
using DynaSharp.Mapping;

[DynamoDbEntity]
[Discriminator("VOTE")]
[PartitionKey("VOTES#{Poll}#{Shard:d2}")]
[SortKey("{At}#{VoteId}")]
public partial record Vote
{
    public required string Poll { get; init; }
    public required int Shard { get; init; }
    public required DateTimeOffset At { get; init; }
    public required Guid VoteId { get; init; }
}

[DynamoDbEntity]
[Discriminator("SESSION")]
[PartitionKey("SESSIONS#{Shard:d2}")]
[SortKey("{Id}")]
public partial record UserSession
{
    public required int Shard { get; init; }
    public required string Id { get; init; }
    public required string User { get; init; }
}

public static class SessionShards
{
    public const int Count = 4;

    // The first byte of a digest is the same in every process, and it is spread evenly.
    public static int Of(string id) => SHA256.HashData(Encoding.UTF8.GetBytes(id))[0] % Count;
}
```

A vote goes to a random shard, and a fan-out query over all shards merges them back in the order of the sort key, here the time:

```csharp
var votes = shop.Of<Vote>();
await votes.Put(new Vote { Poll = "best-pet", Shard = Random.Shared.Next(4), At = DateTimeOffset.UtcNow, VoteId = Guid.NewGuid() }).SendAsync(ct);

var shards = Enumerable.Range(0, 4).Select(shard => Vote.Keys.Pk.Of("best-pet", shard)).ToList();
int total = await votes.Query(shards).CountAsync(ct);
await foreach (var vote in votes.Query(shards).ItemsAsync(ct))
{
    Console.WriteLine(vote.At);
}
```

A session goes to the shard of its id, so a read finds it with one get:

```csharp
var sessions = shop.Of<UserSession>();
await sessions.Put(new UserSession { Shard = SessionShards.Of("session-1"), Id = "session-1", User = "alice" }).SendAsync(ct);

var found = await sessions.Get(UserSession.Keys.Of(SessionShards.Of("session-1"), "session-1")).ItemAsync(ct);
```

Caveats:

- The hash must be stable: `string.GetHashCode()` differs from one process to the next.
- A read of a randomly sharded key costs a query per shard. More shards take more writes and cost more per read.
- A counter is sharded the same way: the [sharded counter](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/ShardedCounterTests.cs) adds to a random shard, and a read sums them.

## Time-bucketed partitions

A stream of samples must not grow one partition without end, so time is cut into buckets, a partition each, and a read of a range asks every bucket it touches and merges them.

```csharp
[DynamoDbEntity]
[Discriminator("SAMPLE")]
[PartitionKey("METRIC#{Metric}#{Bucket:d10}")]
[SortKey("{At}")]
public partial record MetricSample
{
    public required string Metric { get; init; }
    public required long Bucket { get; init; }
    public required DateTimeOffset At { get; init; }
    public required int Value { get; init; }
}
```

The bucket is the time divided by the size of a bucket, a number, so the size is a setting and not part of the template:

```csharp
var size = TimeSpan.FromHours(6);
long BucketOf(DateTimeOffset at) => at.UtcTicks / size.Ticks;

var samples = shop.Of<MetricSample>();
var now = DateTimeOffset.UtcNow;
await samples.Put(new MetricSample { Metric = "cpu", Bucket = BucketOf(now), At = now, Value = 42 }).SendAsync(ct);

var from = now.AddDays(-1);
var buckets = new List<PartitionKey<MetricSample>>();
for (var bucket = BucketOf(from); bucket <= BucketOf(now); bucket++)
{
    buckets.Add(MetricSample.Keys.Pk.Of("cpu", bucket));
}

var lastDay = samples.Query(buckets, MetricSample.Keys.Sk.Between(from, now)).ItemsAsync(ct);
```

Caveats:

- The size of a bucket is the trade: a smaller one spreads the writes and costs more queries per read.
- A reader has to know the size the items were written with, so keep it.
- All writes of one moment go to one bucket. A stream hotter than one partition also needs [write sharding](#write-sharding).

## Large objects

An item holds at most 400 KB, and DynamoDB rejects a larger put with a `ValidationException`. A larger value is cut into chunks in one partition, with a manifest that is written last, so a reader that finds the manifest finds every chunk.

```csharp
[DynamoDbEntity]
[Discriminator("CHUNK")]
[PartitionKey("FILE#{FileId}")]
[SortKey("CHUNK#{Index:d4}")]
public partial record FileChunk
{
    public required string FileId { get; init; }
    public required int Index { get; init; }
    public required ReadOnlyMemory<byte> Data { get; init; }
}

[DynamoDbEntity]
[Discriminator("MANIFEST")]
[PartitionKey("FILE#{FileId}")]
[SortKey("MANIFEST")]
public partial record FileManifest
{
    public required string FileId { get; init; }
    public required int Chunks { get; init; }
}
```

```csharp
byte[] content = await File.ReadAllBytesAsync("report.pdf", ct);
var chunks = content.Chunk(300 * 1024).Select((part, index) => new FileChunk { FileId = "report", Index = index, Data = part }).ToList();
await shop.Of<FileChunk>().BatchWrite().PutAll(chunks).SendAsync(ct);
await shop.Of<FileManifest>().Put(new FileManifest { FileId = "report", Chunks = chunks.Count }).SendAsync(ct);
```

```csharp
var manifest = await shop.Of<FileManifest>().Get(FileManifest.Keys.Of("report")).Consistent().ItemAsync(ct);
if (manifest is not null)
{
    var loaded = new List<byte>();
    await foreach (var chunk in shop.Of<FileChunk>().Query(FileChunk.Keys.Pk.Of("report"), FileChunk.Keys.Sk.BeginsWith()).Consistent().ItemsAsync(ct))
    {
        loaded.AddRange(chunk.Data.Span);
    }
}
```

Caveats:

- The chunks and the manifest are not one transaction. A writer that stops halfway leaves chunks without a manifest, which a reader takes for no file.
- A rewrite with fewer chunks leaves the old ones behind, and this reader, which takes every chunk of the partition, appends them. Delete the chunks past the new count, or read only the `Chunks` that the manifest names.
- A reader during a rewrite can get chunks of the old and the new version. To prevent it, put a version in the key of a chunk, write the chunks of the new version first and switch the manifest to it last.
- A read pays for every chunk. Beyond a few MB, an object in S3 with its key in the item is the better design.
