# Single table and indexes

Items that are read together share a partition, whatever their type, and a global index gives a second way in. These patterns share the indexes `GSI1` and `GSI2` of one table, whose keys are the attributes `gsi1pk`, `gsi1sk`, `gsi2pk` and `gsi2sk`. `shop` is the table of [Design patterns](patterns.md).

A global index is eventually consistent. An item shows up in it, moves or leaves it a moment after the write, and a query of an index cannot be `Consistent()`.

## Adjacency list and inverted index

A graph in one table: users, teams, and the memberships between them. A node and the edges that leave it share a partition, so one query reads a user and their memberships, each as its type. A global index with the key the other way round walks the edges backwards, from a team to its members.

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

`Of<A, B>()` reads each item of a query as what it is. The memberships of a user come first, because `GROUP#` sorts before `PROFILE`:

```csharp
await foreach (var item in shop.Query(Member.Keys.Pk.Of("alice")).Of<Member, Membership>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(member => member.Name, membership => membership.GroupId));
}
```

In the index, the partition of a team holds the team under `INFO` and a membership per user:

```csharp
var byGroup = shop.Index<Membership>(Membership.Indexes.Gsi1);
await foreach (var item in byGroup.Query(Team.Keys.Gsi1Pk.Of("staff")).Of<Team, Membership>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(team => team.Title, membership => membership.UserId));
}
```

Caveats:

- A query of a table opened for one type, such as `shop.Of<Membership>()`, leaves out the items of other types, but DynamoDB reads them and charges for them: the profile counts in `ScannedCount`. A sort key condition such as `Membership.Keys.Sk.BeginsWith()` reads only the memberships.
- The index copies every membership, so each edge costs a write in the table and one in the index.

## Index overloading

A table has few global indexes, so each type gives the same key attributes a meaning of its own, and one index answers a question per type: the staff of a team by the date of hire, the expenses of a team by amount, the warehouses of a city by name.

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

Each type reads the index through its own key builders. `Gsi1Sk.BeginsWith()` is the prefix `HIRED#`, so it reads the staff of the team and none of its expenses, oldest hire first:

```csharp
var hires = shop.Of<Staffer>().Index(Staffer.Indexes.Gsi1)
    .Query(Staffer.Keys.Gsi1Pk.Of("red"), Staffer.Keys.Gsi1Sk.BeginsWith())
    .ItemsAsync(ct);

var inBern = shop.Of<Warehouse>().Index(Warehouse.Indexes.Gsi1).Query(Warehouse.Keys.Gsi1Pk.Of("Bern")).ItemsAsync(ct);
```

Where two types share a partition of the index, one query reads both, the expenses first, because `AMOUNT#` sorts before `HIRED#`:

```csharp
var byTeam = shop.Index<Staffer>(Staffer.Indexes.Gsi1);
await foreach (var item in byTeam.Query(Staffer.Keys.Gsi1Pk.Of("red")).Of<Staffer, Expense>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(staffer => staffer.Id, expense => $"{expense.Id}: {expense.Amount}"));
}
```

Caveats:

- The prefix of each sort key keeps the types of a shared partition apart. Without a condition on it, a typed query reads the other types too and pays for them.
- `{Amount:n8.2}` has a sign and a fixed width, so a refund of -15.50 sorts before an expense of 40.

## Sparse index

An index holds only the items that have its key attributes, so a rare state of a large table, such as the paying subscribers, is read at the cost of what is in that state. With `Sparse = true`, an item whose inputs are null writes no key of the index.

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

`Set` of the inputs takes an item into the index, and `Remove` of one of them takes it out. The item stays in the table:

```csharp
var subscribers = shop.Of<Subscriber>();
await subscribers.Update(Subscriber.Keys.Of("s1"))
    .Set(Subscriber.Attrs.Plan, "pro")
    .Set(Subscriber.Attrs.Since, DateOnly.FromDateTime(DateTime.UtcNow))
    .SendAsync(ct);

var pro = subscribers.Index(Subscriber.Indexes.Gsi2).Query(Subscriber.Keys.Gsi2Pk.Of("pro")).ItemsAsync(ct);

await subscribers.Update(Subscriber.Keys.Of("s1")).Remove(Subscriber.Attrs.Plan).SendAsync(ct);
```

A partition key without a placeholder makes the index one list, such as the featured articles by day:

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

- Membership is the presence of the key attributes, nothing else. A `Set` of null of one input takes the item out, as a `Remove` does.
- A constant partition key is one partition of the index, with its limits. A list that many items enter needs [write sharding](patterns-keys.md#write-sharding).

## Status-keyed index

"My open items, oldest first": a global index over the owner, with the status and the age behind it in the sort key, reads that with one query.

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
[GlobalIndexKey("GSI1", PartitionKey = "OWNER#{Owner}", SortKey = "{Status}#{CreatedAt}")]
public partial record WorkItem
{
    public required string Id { get; init; }
    public required string Owner { get; init; }
    public required WorkStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
```

```csharp
var items = shop.Of<WorkItem>();
var open = items.Index(WorkItem.Indexes.Gsi1)
    .Query(WorkItem.Keys.Gsi1Pk.Of("ann"), WorkItem.Keys.Gsi1Sk.BeginsWith(WorkStatus.Open))
    .ItemsAsync(ct);
```

A change of the status rewrites the sort key of the index, so the update sets every input of that key, also the time that did not change:

```csharp
var items = shop.Of<WorkItem>();
var item = await items.Get(WorkItem.Keys.Of("i1")).ItemAsync(ct);
await items.Update(WorkItem.Keys.Of("i1"))
    .Set(WorkItem.Attrs.Status, WorkStatus.Closed)
    .Set(WorkItem.Attrs.CreatedAt, item!.CreatedAt)
    .SendAsync(ct);
```

Caveats:

- An update that sets `Status` alone throws `InvalidOperationException`, which names `CreatedAt`, before anything is sent, rather than leave the index behind. A `Put` of the changed item writes the whole key.
- A change of status moves the item in the index: DynamoDB deletes the old entry and writes a new one.

## Soft delete

A deleted item stays, with a mark, and leaves a sparse index that lists what is live. Undo stays possible, and the lists need no filter.

```csharp
[DynamoDbEntity]
[Discriminator("NOTE")]
[PartitionKey("NOTE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI2", PartitionKey = "OWNER#{Owner}", SortKey = "LIVE#{LiveSince}", Sparse = true)]
public partial record Note
{
    public required string Id { get; init; }
    public required string Owner { get; init; }
    public DateTimeOffset? LiveSince { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}
```

The delete sets the mark and removes an input of the index key, under a condition that keeps the time of the first delete:

```csharp
var notes = shop.Of<Note>();
await notes.Update(Note.Keys.Of("n1"))
    .Set(Note.Attrs.DeletedAt, DateTimeOffset.UtcNow)
    .Remove(Note.Attrs.LiveSince)
    .If(Note.Attrs.DeletedAt.NotExists())
    .SendAsync(ct);

var live = notes.Index(Note.Indexes.Gsi2).Query(Note.Keys.Gsi2Pk.Of("ann")).ItemsAsync(ct);
```

A restore writes the key of the index again, from both of its inputs, also the owner that never changed:

```csharp
await shop.Of<Note>().Update(Note.Keys.Of("n1"))
    .Remove(Note.Attrs.DeletedAt)
    .Set(Note.Attrs.Owner, "ann")
    .Set(Note.Attrs.LiveSince, DateTimeOffset.UtcNow)
    .SendAsync(ct);
```

Caveats:

- The table knows nothing of the mark. A `Get` or a query of the table still finds a deleted item, so each reader there looks at `DeletedAt`.
- A second delete fails its condition with `ConditionalCheckFailedException`.
