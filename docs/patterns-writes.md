# Safe writes

Writes that race, or that a client sends twice, must still leave the right state. `shop` is the table of [Design patterns](patterns.md).

## Optimistic locking

A read-modify-write needs no lock if the write holds only when nobody wrote since the read. `[Version]` turns this on for a type.

```csharp
[DynamoDbEntity]
[Discriminator("ACCOUNT")]
[PartitionKey("ACCOUNT#{Id}")]
[SortKey("BALANCE")]
public partial record Account
{
    public required string Id { get; init; }
    public decimal Balance { get; init; }

    [Version]
    public long Revision { get; init; }
}
```

A `Put` fails with `ConditionalCheckFailedException` if the stored version moved on since the read. `RetryOnConflictAsync` then reruns the delegate, which reads again:

```csharp
var accounts = shop.Of<Account>();
await client.RetryOnConflictAsync(async token =>
{
    var account = await accounts.Get(Account.Keys.Of("alice")).Consistent().ItemAsync(token);
    await accounts.Put(account! with { Balance = account.Balance + 10m }).SendAsync(token);
}, ct);
```

Caveats:

- Opt-in per type: two writers of a type without `[Version]` both succeed, and the second silently undoes the first.
- Enforced per write: an `Update` or `Delete` by key says `IfVersion(n)` or `IgnoreVersion()`, or throws before sending, see [Optimistic locking](commands.md#optimistic-locking).
- Nothing retries a conflict by itself. `RetryOnConflictAsync` makes 5 runs. With n writers that each write once, a run loses at most n - 1 times, but while new writers keep arriving it can lose any number of times: pass `maxAttempts` for a hot item.

## Uniqueness constraints

DynamoDB keeps only a primary key unique. A value that must be unique, such as an email address, gets a claim item keyed by that value, written in one transaction with the record. With several values, each claim's marker tells which one was taken.

```csharp
public enum Registration
{
    Done,
    EmailTaken,
    HandleTaken,
}

[DynamoDbEntity]
[Discriminator("REGISTRANT")]
[PartitionKey("USER#{Id}")]
[SortKey("REGISTRATION")]
public partial record Registrant
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string Handle { get; init; }
}

[DynamoDbEntity]
[Discriminator("MAIL_CLAIM")]
[PartitionKey("MAIL#{Address}")]
[SortKey("CLAIM")]
public partial record MailClaim
{
    public required string Address { get; init; }
    public required string Owner { get; init; }
}

[DynamoDbEntity]
[Discriminator("HANDLE_CLAIM")]
[PartitionKey("HANDLE#{Handle}")]
[SortKey("CLAIM")]
public partial record HandleClaim
{
    public required string Handle { get; init; }
    public required string Owner { get; init; }
}
```

```csharp
var outcome = await RegisterAsync(new Registrant { Id = "bob", Email = "bob@example.ch", Handle = "bobby" });

Task<Registration> RegisterAsync(Registrant user) => client.RetryOnConflictAsync(async token =>
{
    TransactMarker emailTaken = default;
    TransactMarker handleTaken = default;
    try
    {
        await client.TransactWrite()
            .Unique(shop.Of<MailClaim>(), new MailClaim { Address = user.Email, Owner = user.Id }, out emailTaken)
            .Unique(shop.Of<HandleClaim>(), new HandleClaim { Handle = user.Handle, Owner = user.Id }, out handleTaken)
            .Put(shop.Of<Registrant>(), user).IfNotExists()
            .SendAsync(token);
        return Registration.Done;
    }
    catch (TransactionCanceledException exception) when (exception.ReasonFor(emailTaken) == TransactionFailure.ConditionalCheckFailed)
    {
        return Registration.EmailTaken;
    }
    catch (TransactionCanceledException exception) when (exception.ReasonFor(handleTaken) == TransactionFailure.ConditionalCheckFailed)
    {
        return Registration.HandleTaken;
    }
}, ConflictKind.TransactionConflict, ct);
```

`ConflictKind.TransactionConflict` reruns the delegate only when another registration of the same value is in flight. A failed condition, here the `IfNotExists` of an existing record, surfaces at once.

Changing the value releases the old claim in the same transaction, on condition that it is the user's:

```csharp
var mails = shop.Of<MailClaim>();
await client.TransactWrite()
    .Delete(mails, MailClaim.Keys.Of("alice@example.ch")).If(MailClaim.Attrs.Owner.Eq("alice"))
    .Unique(mails, new MailClaim { Address = "alice@example.org", Owner = "alice" }, out _)
    .Update(shop.Of<Registrant>(), Registrant.Keys.Of("alice")).Set(Registrant.Attrs.Email, "alice@example.org")
    .SendAsync(ct);
```

Caveats:

- `Unique` is a put with `attribute_not_exists` on the key, see [TransactWriteItems](transactions.md#transactwriteitems). A transaction costs twice the capacity of its writes.
- Which retry: plain `RetryOnConflictAsync` also repeats on a failed condition, right for a read-modify-write that reads again, as with [optimistic locking](#optimistic-locking). A run that reads nothing, or turns every expected failed condition into its result, takes `ConflictKind.TransactionConflict`, so a failed condition is its answer, not asked 5 times.

## Idempotency keys

A client retries with a key it chose; the second call does nothing and answers like the first. A record of the key, holding the answer, is written in the effect's transaction and removed later by time to live.

```csharp
[DynamoDbEntity]
[Discriminator("IDEMPOTENCY")]
[PartitionKey("IDEMPOTENCY#{Key}")]
[SortKey("RECORD")]
public partial record IdempotencyRecord
{
    public required string Key { get; init; }
    public required string Answer { get; init; }

    [TimeToLive]
    [DynamoDbAttribute("ttl")]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public required DateTimeOffset ExpiresAt { get; init; }
}

[DynamoDbEntity]
[Discriminator("CHARGE")]
[PartitionKey("CHARGES")]
[SortKey("{Id}")]
public partial record Charge
{
    public required string Id { get; init; }
}
```

The record takes the key unless an unexpired record holds it; then the call answers with that record:

```csharp
var records = shop.Of<IdempotencyRecord>();
var horizon = TimeSpan.FromHours(24);
var now = DateTimeOffset.UtcNow;
var requestKey = "request-1";
var chargeId = "charge-1";

string answer = await client.RetryOnConflictAsync(async token =>
{
    TransactMarker answered = default;
    try
    {
        await client.TransactWrite()
            .Put(records, new IdempotencyRecord { Key = requestKey, Answer = chargeId, ExpiresAt = now + horizon }, out answered)
            .If(IdempotencyRecord.Attrs.ExpiresAt.NotExistsOrLt(now))
            .ReturnOldOnFailure()
            .Put(shop.Of<Charge>(), new Charge { Id = chargeId })
            .SendAsync(token);
        return chargeId;
    }
    catch (TransactionCanceledException exception) when (exception.ReasonFor(answered) == TransactionFailure.ConditionalCheckFailed)
    {
        return exception.ItemFor(answered)!.To<IdempotencyRecord>().Answer;
    }
}, ct);
```

`[TimeToLive]` makes `CreateTable()` turn time to live on for `ExpiresAt`, see [Time to live](entities.md#time-to-live).

Caveats:

- The time to live must exceed the client's retry horizon; after it, a retry counts as a new call.
- DynamoDB deletes an expired item up to days later, so the condition checks the time, not the record's absence. Never use time to live as a timer.
- A conflict means the first call is still running; `RetryOnConflictAsync` asks again and gets its answer.
- Every charge goes to the one partition `CHARGES`, limited to one partition's writes. A busier table gives each charge its own key, such as `CHARGE#{Id}`.

## Events that arrive out of order

An ingest from Kafka, or from any queue that delivers at least once, sees an event late, twice, or after a newer one of the same key. One item per key holds the latest state, and every write carries the version it comes from: a sequence of the producer, or the time of the event. The write holds only where the stored version is older or missing, so the item never moves back, and a failed condition says that the event is old news or stored already.

Where events of different kinds change different fields, each group of fields keeps its own version beside it, here the time of the event that set it:

```csharp
[DynamoDbEntity]
[Discriminator("LIVE_MATCH")]
[PartitionKey("MATCH#{Id}")]
[SortKey("LIVE")]
public partial record LiveMatch
{
    public required string Id { get; init; }
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public DateTimeOffset? ScoreAt { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? StatusAt { get; init; }
}

public sealed record ScoreChanged(string MatchId, int Home, int Away, DateTimeOffset At);

public enum EventOutcome { Applied, AlreadyStored, Superseded }
```

An update sets the fields of its group and their time, on condition that the stored time is older. `NotExistsOrLt(v)` is `NotExists().Or(Lt(v))`: an item that does not exist yet passes, and so does one that has a status and no score yet. `TrySendAsync` takes the failed condition as an answer instead of an exception, and `ReturnOldOnFailure()` hands over the item the condition saw, so the stored time tells the two kinds of failure apart:

```csharp
var matches = shop.Of<LiveMatch>();

async Task<EventOutcome> ApplyAsync(ScoreChanged change, CancellationToken token)
{
    var result = await matches.Update(LiveMatch.Keys.Of(change.MatchId))
        .Set(LiveMatch.Attrs.HomeGoals, change.Home)
        .Set(LiveMatch.Attrs.AwayGoals, change.Away)
        .Set(LiveMatch.Attrs.ScoreAt, change.At)
        .If(LiveMatch.Attrs.ScoreAt.NotExistsOrLt(change.At))
        .ReturnOldOnFailure()
        .TrySendAsync(token);
    if (result.ConditionMet)
    {
        return EventOutcome.Applied;
    }

    // The stored time is not older. The same time: this event is stored, so the consumer commits it as an applied one.
    return result.Item!.ScoreAt == change.At ? EventOutcome.AlreadyStored : EventOutcome.Superseded;
}
```

A status event is the same update over `Status` and `StatusAt`, so a late score never undoes a newer status and the other way around. Where every event carries the whole state, one version for the item is enough, and a `Put` replaces it: `.Put(standing).If(Standing.Attrs.Seq.NotExistsOrLt(standing.Seq))`.

Caveats:

- `Lt`, not `Le`: the same version a second time is a duplicate and writes nothing. `NotExistsOrLe` writes it again, which is harmless for the same state, but cannot tell a duplicate from an applied event.
- An equal time is a second delivery of the event, or the client's own retry after a lost response, which meets what its first attempt wrote. The two look the same on the wire, so no client can tell them apart, and both are safe to commit: the event is stored once. That is why the write needs no `MaxAttempts(1)`.
- A failed condition is the answer, not a conflict, so the write takes no `RetryOnConflictAsync`, which would run it five times for nothing. A failed condition of `TrySendAsync` is no conflict to it anyway, and no error on the span either, see [Observability](observability.md#traces).
- A version orders only what one source numbers. Times from several producers are as good as their clocks, and offsets of Kafka compare within one partition only, so a key has to stay on one partition, also when partitions are added.
- One event that changes two groups is two updates, or one with both conditions joined by `And`, and then a newer value of one group blocks the older event for both.
- A delete would let a late event bring the item back. Keep the item with a deleted flag and the version instead, and let time to live take it later, see [soft delete](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/SoftDeleteTests.cs).
- A failed condition costs a write unit, as the write would. [The recipe](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/OutOfOrderEventsTests.cs) runs late and repeated events against every backend.

## Counters

`ADD` counts on the server, so concurrent writers need no read or lock, and `Return` gives each the value after its own add.

```csharp
[DynamoDbEntity]
[Discriminator("PAGE_VIEWS")]
[PartitionKey("PAGE#{Path}")]
[SortKey("VIEWS")]
public partial record PageViews
{
    public required string Path { get; init; }
    public long Count { get; init; }
}
```

```csharp
long views = await shop.Of<PageViews>().Update(PageViews.Keys.Of("/home"))
    .Add(PageViews.Attrs.Count, 1L)
    .MaxAttempts(1)
    .Return(PageViews.Attrs.Count)
    .SendAsync(ct);
```

A retry counts twice when the response was lost after the write took effect. `MaxAttempts(1)` counts at most once, so it can undercount: after a failure the caller cannot tell whether it counted.

An idempotent counter remembers the ids it counted, and the write holds only for a new one. The set of ids is windowed, an item per day:

```csharp
[DynamoDbEntity]
[Discriminator("TALLY")]
[PartitionKey("TALLY#{Name}")]
[SortKey("DAY#{Day}")]
public partial record DailyTally
{
    public required string Name { get; init; }
    public required DateOnly Day { get; init; }
    public long Count { get; init; }
    public HashSet<string> Seen { get; init; } = [];
}
```

```csharp
var requestId = "request-1";
try
{
    await shop.Of<DailyTally>().Update(DailyTally.Keys.Of("signups", DateOnly.FromDateTime(DateTime.UtcNow)))
        .Add(DailyTally.Attrs.Count, 1L)
        .Add(DailyTally.Attrs.Seen, [requestId])
        .If(DailyTally.Attrs.Seen.Contains(requestId).Negate())
        .SendAsync(ct);
}
catch (ConditionalCheckFailedException)
{
    // Counted before, so this was a retry.
}
```

`Contains` on a set takes an element, and `Negate()` puts `NOT` in front. A missing set holds nothing, so the first write of a window passes.

Caveats:

- One item takes about 1000 writes a second; [shard](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/ShardedCounterTests.cs) a hotter counter.
- A write is billed on the larger of the old and new item, so the cost grows with the set: with ten ids of 300 bytes an add costs 4 write units, where the first cost 1. Keep the window small.
- On a busy day the set reaches the 400 KB item limit, and every further write that day fails with a `ValidationException`.
- The window is the day the caller builds the key. A caller's retry across midnight UTC lands in the next day's item, which has not seen the id, and counts twice. The client's own retries resend the same key.
- An atomic counter has gaps where a write failed. For numbers without gaps, as invoices need, see the [gapless sequence](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/GaplessSequenceTests.cs).

## Hi/lo sequence

Ids that are numbers, unique across processes, without a write per id: one `ADD` claims a block, and the process hands the block out from memory.

```csharp
[DynamoDbEntity]
[Discriminator("HILO")]
[PartitionKey("HILO#{Name}")]
[SortKey("HILO")]
public partial record HiLoBlock
{
    public required string Name { get; init; }
    public long Hi { get; init; }
}
```

```csharp
const int BlockSize = 100;
long hi = await shop.Of<HiLoBlock>().Update(HiLoBlock.Keys.Of("orders"))
    .Add(HiLoBlock.Attrs.Hi, (long)BlockSize)
    .Return(HiLoBlock.Attrs.Hi)
    .SendAsync(ct);

// This process owns hi - BlockSize + 1 to hi, and claims the next block when they are used up.
var first = hi - BlockSize + 1;
```

The numbers are unique, because each `ADD` returns a value no other call gets. They have gaps: a process that stops leaves the rest of its block unused, and a retry of the client after a lost response claims a block twice and wastes one, which is why this write needs no `MaxAttempts(1)`. Gaps are fine for ids and wrong for invoice numbers, which take the [gapless sequence](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/GaplessSequenceTests.cs). [The recipe](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/HiLoAllocatorTests.cs) has a thread-safe allocator.

## Transactional outbox

A state change and its event are written in one transaction, so neither happens without the other.

```csharp
[DynamoDbEntity]
[Discriminator("PAYMENT")]
[PartitionKey("PAYMENT#{Id}")]
[SortKey("STATE")]
public partial record Payment
{
    public required string Id { get; init; }
}

[DynamoDbEntity]
[Discriminator("OUTBOX")]
[PartitionKey("OUTBOX")]
[SortKey("{At}#{EventId}")]
public partial record OutboxEvent
{
    public required DateTimeOffset At { get; init; }
    public required Guid EventId { get; init; }
    public required string PaymentId { get; init; }
}
```

```csharp
await client.TransactWrite()
    .Put(shop.Of<Payment>(), new Payment { Id = "p1" }).IfNotExists()
    .Put(shop.Of<OutboxEvent>(), new OutboxEvent { At = DateTimeOffset.UtcNow, EventId = Guid.NewGuid(), PaymentId = "p1" })
    .SendAsync(ct);
```

A relay reads the outbox in time order, publishes each event and deletes it:

```csharp
var outbox = shop.Of<OutboxEvent>();
foreach (var pending in await outbox.Query(OutboxEvent.Keys.Pk.Of()).Consistent().ItemsAsync(ct).ToListAsync())
{
    Console.WriteLine($"Paid: {pending.PaymentId}, event {pending.EventId}");
    await outbox.Delete(pending).SendAsync(ct);
}
```

Caveats:

- At least once: a relay that stops between publish and delete publishes the event again, so consumers deduplicate by `EventId`.
- The outbox is one partition, limited to one partition's writes.
- Often the [table's stream](streams.md) already is this outbox, without the item and the relay.
