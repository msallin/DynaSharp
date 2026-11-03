# Safe writes

A write that races another, or that a client sends twice, must still leave the right state. These patterns use conditions, `[Version]` and transactions for it. `shop` is the table of [Design patterns](patterns.md).

## Optimistic locking

A read-modify-write stays correct without a lock, because the write holds only if nobody wrote since the read. `[Version]` turns it on for a type.

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

A `Put` carries the version it read, and fails with `ConditionalCheckFailedException` if the stored one moved on. `RetryOnConflictAsync` then runs the delegate again, which reads again:

```csharp
var accounts = shop.Of<Account>();
await client.RetryOnConflictAsync(async token =>
{
    var account = await accounts.Get(Account.Keys.Of("alice")).Consistent().ItemAsync(token);
    await accounts.Put(account! with { Balance = account.Balance + 10m }).SendAsync(token);
}, ct);
```

Caveats:

- It is opt-in per type. Two writers of a type without `[Version]` both succeed, and the second silently undoes the first.
- It is enforced per write. An `Update` or a `Delete` by key says `IfVersion(n)` or `IgnoreVersion()`, or it throws before anything is sent, see [Optimistic locking](commands.md#optimistic-locking).
- Nothing retries a conflict by itself. `RetryOnConflictAsync` makes 5 runs, and with n writers on one item a run can lose up to n - 1 times, so pass `maxAttempts` for a hot item.

## Uniqueness constraints

DynamoDB keeps only a primary key unique. A value that must be unique, such as an email address or a handle, gets a claim item whose key is that value, written in one transaction with the record. With several values, the marker of each claim tells which one was taken.

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

async Task<Registration> RegisterAsync(Registrant user)
{
    TransactMarker emailTaken = default;
    TransactMarker handleTaken = default;
    while (true)
    {
        try
        {
            await client.TransactWrite()
                .Unique(shop.Of<MailClaim>(), new MailClaim { Address = user.Email, Owner = user.Id }, out emailTaken)
                .Unique(shop.Of<HandleClaim>(), new HandleClaim { Handle = user.Handle, Owner = user.Id }, out handleTaken)
                .Put(shop.Of<Registrant>(), user).IfNotExists()
                .SendAsync(ct);
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
        catch (TransactionCanceledException exception) when (exception.Failures.Any(failure => failure.Reason == TransactionFailure.TransactionConflict))
        {
            // Another registration of the same value is in flight: only asking again tells who got it.
        }
    }
}
```

A change of the value releases the old claim in the same transaction, under a condition that it is the user's:

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
- The loop repeats only a `TransactionConflict`. `RetryOnConflictAsync` would also repeat a failed condition, such as the `IfNotExists` of the record, where asking again changes nothing.

## Idempotency keys

A client retries with a key it chose, and the second call does nothing and answers like the first. A record of the key goes into the same transaction as the effect and holds the answer, and time to live removes it later.

```csharp
[DynamoDbEntity]
[Discriminator("IDEMPOTENCY")]
[PartitionKey("IDEMPOTENCY#{Key}")]
[SortKey("RECORD")]
public partial record IdempotencyRecord
{
    public required string Key { get; init; }
    public required string Answer { get; init; }

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

The record takes the key unless a record holds it that has not expired. If one does, the call answers with what that record says:

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
            .If(IdempotencyRecord.Attrs.ExpiresAt.NotExists().Or(IdempotencyRecord.Attrs.ExpiresAt.Lt(now)))
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

The table turns time to live on for the attribute with `.TimeToLive(IdempotencyRecord.Attrs.ExpiresAt)`, see [Creating a table](tables.md#creating-a-table).

Caveats:

- The time to live must be longer than the retry horizon of the client. After it, a retry counts as a new call.
- DynamoDB deletes an expired item up to days later, so the condition goes by the time and not by the absence of the record. Never use time to live as a timer.
- A conflict means that the first call is still running. `RetryOnConflictAsync` asks again, and then gets its answer.

## Counters

`ADD` counts on the server, so concurrent writers need no read and no lock, and `Return` gives each of them the value after its own add.

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

A retry counts twice: when the response is lost after the write took effect, the client sends the add again. `MaxAttempts(1)` makes an exact counter, and a failure then leaves the caller unable to tell whether it counted.

A counter that a retry cannot count twice remembers the ids it has counted, and the write holds only for a new one. The set of ids is windowed, an item per day:

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
        .If($"NOT contains({DailyTally.Attrs.Seen}, {requestId})")
        .SendAsync(ct);
}
catch (ConditionalCheckFailedException)
{
    // Counted before, so this was a retry.
}
```

Caveats:

- One item takes about 1000 writes a second. A hotter counter is [sharded](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/ShardedCounterTests.cs).
- A write is billed on the larger of the old and the new item, so the cost of the idempotent counter grows with its set: with ten ids of 300 bytes, an add costs 4 write units where the first cost 1. Keep the window small.
- Numbers from an atomic counter have gaps where a write failed. Numbers without gaps, as invoices need, are the [gapless sequence](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/GaplessSequenceTests.cs).

## Transactional outbox

A change of state and the event about it are written in one transaction, so neither happens without the other. A relay publishes what it finds and deletes it.

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

The relay reads the outbox in order of time, publishes each event and deletes it:

```csharp
var outbox = shop.Of<OutboxEvent>();
foreach (var pending in await outbox.Query(OutboxEvent.Keys.Pk.Of()).Consistent().ItemsAsync(ct).ToListAsync(ct))
{
    Console.WriteLine($"Paid: {pending.PaymentId}, event {pending.EventId}");
    await outbox.Delete(pending).SendAsync(ct);
}
```

Caveats:

- It delivers at least once. A relay that stops after the publish and before the delete publishes the same event again, so a consumer goes by `EventId`.
- The outbox is one partition, which takes the writes of one.
- Often the [stream of the table](streams.md) already is this outbox, which saves the item and the relay.
