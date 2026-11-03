using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Status-keyed GSI ("my open items")</see>: a global index over owner and status, with the age behind it, reads "my open items, oldest first". The age is a
/// version-7 id, which starts with the time it was made and is an input of the key of the table, so a change of the status rewrites the key of the index without a read: the update knows the id from
/// its key. An index key with a time of its own needs that time from the caller, because DynamoDB cannot build a key from what an item holds.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class StatusKeyedIndexTests(Backend backend)
{
    private const string Ann = "ann";

    private static readonly DateTimeOffset Midnight = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Query_MyOpenItems_ReadsOldestFirstAndNoOtherStatus()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var items = sandbox.Shop.Of<WorkItem>();
        Guid[] ids = [Guid.CreateVersion7(Midnight.AddDays(-1)), Guid.CreateVersion7(Midnight), Guid.CreateVersion7(Midnight)];
        await items.Put(NewItem(ids[1], WorkStatus.Open)).SendAsync();
        await items.Put(NewItem(ids[0], WorkStatus.Open)).SendAsync();
        await items.Put(NewItem(ids[2], WorkStatus.Closed)).SendAsync();

        // Act
        var open = await OpenItemsAsync(items);

        // Assert
        await Assert.That(Positions(open, ids)).IsEqualTo("0,1");
    }

    [Test]
    public async Task Query_OpenItemsOfADay_ReadsThatDayByTheBoundsOfItsIds()
    {
        // Arrange (one item in the last millisecond before the day, one at each end of the day, one in the first millisecond after it)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var items = sandbox.Shop.Of<WorkItem>();
        var end = Midnight.AddDays(1).AddMilliseconds(-1);
        Guid[] ids = [Guid.CreateVersion7(Midnight.AddMilliseconds(-1)), Guid.CreateVersion7(Midnight), Guid.CreateVersion7(end), Guid.CreateVersion7(end.AddMilliseconds(1))];
        foreach (var id in ids)
        {
            await items.Put(NewItem(id, WorkStatus.Open)).SendAsync();
        }

        // Act
        var range = WorkItem.Keys.Gsi1Sk.Between(WorkStatus.Open, Guid.FirstVersion7(Midnight), Guid.LastVersion7(end));
        var read = await items.Index(WorkItem.Indexes.Gsi1).Query(WorkItem.Keys.Gsi1Pk.Of(Ann), range).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(Positions(read, ids)).IsEqualTo("1,2");
    }

    [Test]
    public async Task Update_NewStatusAlone_MovesTheItemInTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var items = sandbox.Shop.Of<WorkItem>();
        var item = NewItem(Guid.CreateVersion7(Midnight), WorkStatus.Open);
        await items.Put(item).SendAsync();

        // Act
        await items.Update(WorkItem.Keys.Of(item.Id)).Set(WorkItem.Attrs.Status, WorkStatus.Closed).IfExists().SendAsync();

        // Assert
        await Assert.That((await OpenItemsAsync(items)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Update_StatusAloneUnderATimeOfItsOwn_ThrowsNamingTheSetToAdd()
    {
        // Arrange (the design with a time of its own in the key of the index: the update has to bring it, and the build says so too)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var items = sandbox.Shop.Of<DatedWorkItem>();

        // Act
#pragma warning disable DYNA226 // The key is left half done on purpose.
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await items.Update(DatedWorkItem.Keys.Of("i1")).Set(DatedWorkItem.Attrs.Status, WorkStatus.Closed).SendAsync());
#pragma warning restore DYNA226

        // Assert
        await Assert.That(failure.Message).Contains(".Set(DatedWorkItem.Attrs.CreatedAt, value) with the value the item holds", StringComparison.Ordinal);
    }

    private static WorkItem NewItem(Guid id, WorkStatus status) => new() { Id = id, Owner = Ann, Status = status };

    // The items by their place in the ids they were made with, in the order of the read.
    private static string Positions(List<WorkItem> read, Guid[] ids) => string.Join(",", read.Select(item => Array.IndexOf(ids, item.Id)));

    private static async Task<List<WorkItem>> OpenItemsAsync(Table<WorkItem> items) =>
        await items.Index(WorkItem.Indexes.Gsi1).Query(WorkItem.Keys.Gsi1Pk.Of(Ann), WorkItem.Keys.Gsi1Sk.BeginsWith(WorkStatus.Open)).ItemsAsync().ToListAsync();
}

internal enum WorkStatus
{
    Open,
    Closed,
}

[DynamoDbEntity]
[Discriminator("WORK_ITEM")]
[PartitionKey("ITEM#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "OWNER#{Owner}", SortKey = "{Status}#{Id}")]
internal sealed partial record WorkItem
{
    public required Guid Id { get; init; }

    public required string Owner { get; init; }

    public required WorkStatus Status { get; init; }
}

[DynamoDbEntity]
[Discriminator("DATED_WORK_ITEM")]
[PartitionKey("ITEM#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "OWNER#{Owner}", SortKey = "{Status}#{CreatedAt}")]
internal sealed partial record DatedWorkItem
{
    public required string Id { get; init; }

    public required string Owner { get; init; }

    public required WorkStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
