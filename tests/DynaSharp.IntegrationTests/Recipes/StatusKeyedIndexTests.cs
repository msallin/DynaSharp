using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Status-keyed GSI ("my open items")</see>: a global index over owner and status, with the age behind it, reads "my open items, oldest first". A change of the status rewrites the key of the index, so the update has
/// to bring every part of that key, also the one that did not change.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class StatusKeyedIndexTests(Backend backend)
{
    private const string Ann = "ann";

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Query_MyOpenItems_ReadsOldestFirstAndNoOtherStatus()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var items = sandbox.Shop.Of<WorkItem>();
        await items.Put(NewItem("newer", WorkStatus.Open, Noon)).SendAsync();
        await items.Put(NewItem("older", WorkStatus.Open, Noon.AddDays(-1))).SendAsync();
        await items.Put(NewItem("done", WorkStatus.Closed, Noon)).SendAsync();

        // Act
        var open = await OpenItemsAsync(items);

        // Assert
        await Assert.That(string.Join(",", open.Select(static item => item.Id))).IsEqualTo("older,newer");
    }

    [Test]
    public async Task Update_NewStatus_MovesTheItemInTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var items = sandbox.Shop.Of<WorkItem>();
        var item = NewItem("i1", WorkStatus.Open, Noon);
        await items.Put(item).SendAsync();

        // Act
        await items.Update(WorkItem.Keys.Of(item.Id)).Set(WorkItem.Attrs.Status, WorkStatus.Closed).Set(WorkItem.Attrs.CreatedAt, item.CreatedAt).SendAsync();

        // Assert
        await Assert.That((await OpenItemsAsync(items)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Update_StatusAloneByHandle_ThrowsRatherThanLeaveTheIndexStale()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var items = sandbox.Shop.Of<WorkItem>();

        // Act
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await items.Update(WorkItem.Keys.Of("i1")).Set(WorkItem.Attrs.Status, WorkStatus.Closed).SendAsync());

        // Assert
        await Assert.That(failure.Message).Contains("CreatedAt", StringComparison.Ordinal);
    }

    private static WorkItem NewItem(string id, WorkStatus status, DateTimeOffset createdAt) => new() { Id = id, Owner = Ann, Status = status, CreatedAt = createdAt };

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
[GlobalIndexKey("GSI1", PartitionKey = "OWNER#{Owner}", SortKey = "{Status}#{CreatedAt}")]
internal sealed partial record WorkItem
{
    public required string Id { get; init; }

    public required string Owner { get; init; }

    public required WorkStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
