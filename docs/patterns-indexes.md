# Single table and indexes

Items read together share a partition, whatever their type, and a global index gives a second way in. These patterns share the indexes `GSI1` and `GSI2` of one table, keyed by `gsi1pk`, `gsi1sk`, `gsi2pk` and `gsi2sk`. `shop` is the table of [Design patterns](patterns.md).

A global index is eventually consistent: an item enters, moves or leaves it a moment after the write, and a query of an index cannot be `Consistent()`.

## Adjacency list and inverted index

A graph in one table: users, teams and memberships. A node shares a partition with its outgoing edges, so one query reads a user and their memberships. A global index with the key reversed walks from a team to its members.

```csharp
[DynamoDbEntity]
[Discriminator("MEMBER")]
[PartitionKey("USER#{UserId}")]
[SortKey("PROFILE")]
public partial record Member
{
    public required string UserId { get; init; }
    public required string Name { get; init; }
}

[DynamoDbEntity]
[Discriminator("MEMBERSHIP")]
[PartitionKey("USER#{UserId}")]
[SortKey("GROUP#{GroupId}")]
[GlobalIndexKey("GSI1", PartitionKey = "GROUP#{GroupId}", SortKey = "USER#{UserId}")]
public partial record Membership
{
    public required string UserId { get; init; }
    public required string GroupId { get; init; }
}

[DynamoDbEntity]
[Discriminator("TEAM")]
[PartitionKey("GROUP#{GroupId}")]
[SortKey("INFO")]
[GlobalIndexKey("GSI1", PartitionKey = "GROUP#{GroupId}", SortKey = "INFO")]
public partial record Team
{
    public required string GroupId { get; init; }
    public required string Title { get; init; }
}
```

`Of<A, B>()` reads each item as its type. Memberships come first, since `GROUP#` sorts before `PROFILE`:

```csharp
await foreach (var item in shop.Query(Member.Keys.Pk.Of("alice")).Of<Member, Membership>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(member => member.Name, membership => membership.GroupId));
}
```

In the index, a team's partition holds the team and a membership per user:

```csharp
var byGroup = shop.Index<Membership>(Membership.Indexes.Gsi1);
await foreach (var item in byGroup.Query(Team.Keys.Gsi1Pk.Of("staff")).Of<Team, Membership>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(team => team.Title, membership => membership.UserId));
}
```

Caveats:

- A table opened for one type, such as `shop.Of<Membership>()`, leaves out other types, but DynamoDB reads and charges for them (the profile counts in `ScannedCount`). A sort key condition such as `Membership.Keys.Sk.BeginsWith()` reads only the memberships.
- The index copies every membership, so each edge costs a write in the table and one in the index.

## Index overloading

A table has few global indexes, so each type gives the same key attributes its own meaning, and one index answers a question per type:

```csharp
[DynamoDbEntity]
[Discriminator("STAFFER")]
[PartitionKey("STAFF#{Id}")]
[SortKey("PROFILE")]
[GlobalIndexKey("GSI1", PartitionKey = "TEAM#{Team}", SortKey = "HIRED#{HiredOn}")]
public partial record Staffer
{
    public required string Id { get; init; }
    public required string Team { get; init; }
    public required DateOnly HiredOn { get; init; }
}

[DynamoDbEntity]
[Discriminator("EXPENSE")]
[PartitionKey("EXPENSE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "TEAM#{Team}", SortKey = "AMOUNT#{Amount:n8.2}")]
public partial record Expense
{
    public required string Id { get; init; }
    public required string Team { get; init; }
    public required decimal Amount { get; init; }
}

[DynamoDbEntity]
[Discriminator("WAREHOUSE")]
[PartitionKey("WAREHOUSE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "CITY#{City}", SortKey = "{Name}")]
public partial record Warehouse
{
    public required string Id { get; init; }
    public required string City { get; init; }
    public required string Name { get; init; }
}
```

`Gsi1Sk.BeginsWith()` is the prefix `HIRED#`, so it reads the team's staff, oldest hire first, and none of its expenses:

```csharp
var hires = shop.Of<Staffer>().Index(Staffer.Indexes.Gsi1)
    .Query(Staffer.Keys.Gsi1Pk.Of("red"), Staffer.Keys.Gsi1Sk.BeginsWith())
    .ItemsAsync(ct);

var inBern = shop.Of<Warehouse>().Index(Warehouse.Indexes.Gsi1).Query(Warehouse.Keys.Gsi1Pk.Of("Bern")).ItemsAsync(ct);
```

Where two types share an index partition, one query reads both, expenses first, since `AMOUNT#` sorts before `HIRED#`:

```csharp
var byTeam = shop.Index<Staffer>(Staffer.Indexes.Gsi1);
await foreach (var item in byTeam.Query(Staffer.Keys.Gsi1Pk.Of("red")).Of<Staffer, Expense>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(staffer => staffer.Id, expense => $"{expense.Id}: {expense.Amount}"));
}
```

Caveats:

- The sort key prefix keeps the types of a shared partition apart. Without a condition on it, a typed query reads the other types too and pays for them.
- `{Amount:n8.2}` has a sign and a fixed width, so a refund of -15.50 sorts before an expense of 40.

## Sparse index

An index holds only items that have its key attributes, so reading a rare state of a large table, such as paying subscribers, costs only what is in that state. With `Sparse = true`, an item with null inputs writes no index key.

```csharp
[DynamoDbEntity]
[Discriminator("SUBSCRIBER")]
[PartitionKey("SUB#{Id}")]
[SortKey("PROFILE")]
[GlobalIndexKey("GSI2", PartitionKey = "PLAN#{Plan}", SortKey = "SINCE#{Since}", Sparse = true)]
public partial record Subscriber
{
    public required string Id { get; init; }
    public string? Plan { get; init; }
    public DateOnly? Since { get; init; }
}
```

`Set` of the inputs puts an item into the index; `Remove` of one takes it out. The item stays in the table:

```csharp
var subscribers = shop.Of<Subscriber>();
await subscribers.Update(Subscriber.Keys.Of("s1"))
    .Set(Subscriber.Attrs.Plan, "pro")
    .Set(Subscriber.Attrs.Since, DateOnly.FromDateTime(DateTime.UtcNow))
    .SendAsync(ct);

var pro = subscribers.Index(Subscriber.Indexes.Gsi2).Query(Subscriber.Keys.Gsi2Pk.Of("pro")).ItemsAsync(ct);

await subscribers.Update(Subscriber.Keys.Of("s1")).Remove(Subscriber.Attrs.Plan).SendAsync(ct);
```

A partition key without a placeholder makes the index one list, such as featured articles by day:

```csharp
[DynamoDbEntity]
[Discriminator("ARTICLE")]
[PartitionKey("ARTICLE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI2", PartitionKey = "FEATURED", SortKey = "ON#{FeaturedOn}", Sparse = true)]
public partial record Article
{
    public required string Id { get; init; }
    public DateOnly? FeaturedOn { get; init; }
}
```

```csharp
var articles = shop.Of<Article>();
await articles.Update(Article.Keys.Of("a1")).Set(Article.Attrs.FeaturedOn, DateOnly.FromDateTime(DateTime.UtcNow)).SendAsync(ct);

var featured = articles.Index(Article.Indexes.Gsi2).Query(Article.Keys.Gsi2Pk.Of()).ItemsAsync(ct);
```

Caveats:

- A `Set` of null of one input takes the item out, as a `Remove` does.
- A constant partition key is one index partition, with its limits. A list that many items enter needs [write sharding](patterns-keys.md#write-sharding).

## Status-keyed index

"My open items, oldest first" is one query of a global index over the owner, with status and age in the sort key. The age is the id: a [version-7 `Guid`](patterns-keys.md#newest-first-with-version-7-guid-sort-keys) starts with its creation time.

```csharp
public enum WorkStatus
{
    Open,
    Closed,
}

[DynamoDbEntity]
[Discriminator("WORK_ITEM")]
[PartitionKey("ITEM#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "OWNER#{Owner}", SortKey = "{Status}#{Id}")]
public partial record WorkItem
{
    public required Guid Id { get; init; }
    public required string Owner { get; init; }
    public required WorkStatus Status { get; init; }
}
```

```csharp
var items = shop.Of<WorkItem>();
var open = items.Index(WorkItem.Indexes.Gsi1)
    .Query(WorkItem.Keys.Gsi1Pk.Of("ann"), WorkItem.Keys.Gsi1Sk.BeginsWith(WorkStatus.Open))
    .ItemsAsync(ct);

var openedThisWeek = items.Index(WorkItem.Indexes.Gsi1)
    .Query(WorkItem.Keys.Gsi1Pk.Of("ann"), WorkItem.Keys.Gsi1Sk.Between(WorkStatus.Open, Guid.FirstVersion7(DateTimeOffset.UtcNow.AddDays(-7)), Guid.LastVersion7(DateTimeOffset.UtcNow)))
    .ItemsAsync(ct);
```

A status change rewrites the index sort key. Its other input, the id, comes with the update's key, so closing an item sets only the status:

```csharp
var items = shop.Of<WorkItem>();
var id = Guid.CreateVersion7();
await items.Put(new WorkItem { Id = id, Owner = "ann", Status = WorkStatus.Open }).SendAsync(ct);

await items.Update(WorkItem.Keys.Of(id)).Set(WorkItem.Attrs.Status, WorkStatus.Closed).IfExists().SendAsync(ct);
```

Caveats:

- DynamoDB cannot build a composite key from what an item holds, so an update brings every input of a key it changes: table key inputs come with the key, others from the caller. With `{Status}#{CreatedAt}`, closing an item also sets `CreatedAt` to its stored value, which takes a read first. Without it the build reports [`DYNA226`](diagnostics.md#commands) and the update throws `InvalidOperationException` naming the `Set` to add, before anything is sent. A `Put` of the changed item writes the whole key.
- A status change moves the item in the index: DynamoDB deletes the old entry and writes a new one.

## Soft delete

A deleted item stays, with a mark, and leaves a sparse index of what is live. Undo stays possible, and the lists need no filter.

```csharp
[DynamoDbEntity]
[Discriminator("NOTE")]
[PartitionKey("OWNER#{Owner}")]
[SortKey("NOTE#{Id}")]
[GlobalIndexKey("GSI2", PartitionKey = "OWNER#{Owner}", SortKey = "LIVE#{LiveSince}", Sparse = true)]
public partial record Note
{
    public required string Owner { get; init; }
    public required string Id { get; init; }
    public DateTimeOffset? LiveSince { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}
```

The delete sets the mark and removes `LiveSince`, which takes the item out of the index; the condition keeps the first delete's time:

```csharp
var notes = shop.Of<Note>();
await notes.Update(Note.Keys.Of("ann", "n1"))
    .Set(Note.Attrs.DeletedAt, DateTimeOffset.UtcNow)
    .Remove(Note.Attrs.LiveSince)
    .If(Note.Attrs.DeletedAt.NotExists())
    .SendAsync(ct);

var live = notes.Index(Note.Indexes.Gsi2).Query(Note.Keys.Gsi2Pk.Of("ann")).ItemsAsync(ct);
```

A restore writes both index keys again; the owner comes with the table key, so it sets only the time:

```csharp
await shop.Of<Note>().Update(Note.Keys.Of("ann", "n1"))
    .Remove(Note.Attrs.DeletedAt)
    .Set(Note.Attrs.LiveSince, DateTimeOffset.UtcNow)
    .IfExists()
    .SendAsync(ct);
```

Caveats:

- The table knows nothing of the mark. A `Get` or table query still finds a deleted item, so each such reader checks `DeletedAt`.
- A second delete fails its condition with `ConditionalCheckFailedException`.
- A table key cannot change, so a note that changes hands is deleted and put again. With the owner outside the table key, `NOTE#{Id}`, a restore must also set `Owner` to the value the item holds: the delete removed both index keys, an update does not read the item to rebuild `OWNER#{Owner}`, and a sparse index takes an item only with both keys. Without it the build reports [`DYNA226`](diagnostics.md#commands). An index whose partition key is the owner alone, `PartitionKey = "{Owner}"`, needs no such `Set`: the restore copies the `Owner` attribute (`SET gsi2pk = Owner`), and a note without an owner fails the restore with `ValidationException` instead of staying out of the list.
- The index has the table's partition key, so a local index would do too, with consistent reads, but DynamoDB creates a local index only with its table.
