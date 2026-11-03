using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Soft delete</see>: an item stays, marked as deleted, and leaves a sparse index that lists what is live, so undo stays possible and list views need no filter. The base table does not know about any of that: a get
/// still finds the item, and each reader there has to look at the mark. The owner is in the key of the table, so a restore sets the time alone: DynamoDB cannot build a key from what an item holds, and with the owner outside the key it has to come from the caller.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class SoftDeleteTests(Backend backend)
{
    private const string Ann = "ann";
    private const string Id = "n1";

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static TimeProvider Clock => new ManualClock(Noon);

    [Test]
    public async Task SoftDeleteAsync_LeavesTheListButABaseTableReadStillSeesTheItem()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var notes = sandbox.Shop.Of<StoredNote>();
        await notes.Put(NewNote()).SendAsync();
        await SoftDeleteAsync(notes, Clock);

        // Act
        var listed = await LiveNotesAsync(notes);
        var read = await notes.Get(StoredNote.Keys.Of(Ann, Id)).Consistent().ItemAsync();

        // Assert
        await Assert.That((listed.Count, read?.DeletedAt)).IsEqualTo((0, (DateTimeOffset?)Noon));
    }

    [Test]
    public async Task SoftDeleteAsync_Twice_FailsTheCondition()
    {
        // Arrange (the time of the first delete stays)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var notes = sandbox.Shop.Of<StoredNote>();
        await notes.Put(NewNote()).SendAsync();
        await SoftDeleteAsync(notes, Clock);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => SoftDeleteAsync(notes, Clock));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ConditionalCheckFailedException");
    }

    [Test]
    public async Task Update_Restore_BringsTheItemBackIntoTheList()
    {
        // Arrange (the owner is in the key of the table, so the restore brings the time alone, and the update writes the other key of the index from the key it was given)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var notes = sandbox.Shop.Of<StoredNote>();
        await notes.Put(NewNote()).SendAsync();
        await SoftDeleteAsync(notes, Clock);

        // Act
        await notes.Update(StoredNote.Keys.Of(Ann, Id))
            .Remove(StoredNote.Attrs.DeletedAt)
            .Set(StoredNote.Attrs.LiveSince, Noon)
            .IfExists()
            .SendAsync();

        // Assert
        await Assert.That((await LiveNotesAsync(notes)).Single().DeletedAt).IsNull();
    }

    [Test]
    public async Task Update_RestoreWithTheOwnerOutsideTheKey_ThrowsNamingTheSetToAdd()
    {
        // Arrange (with a key of its own the owner has to come from the caller, and the build says so too)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        var notes = sandbox.Shop.Of<LooseNote>();

        // Act
#pragma warning disable DYNA226 // The other key of the index is left out on purpose.
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await notes.Update(LooseNote.Keys.Of(Id)).Remove(LooseNote.Attrs.DeletedAt).Set(LooseNote.Attrs.LiveSince, Noon).SendAsync());
#pragma warning restore DYNA226

        // Assert
        await Assert.That(failure.Message).Contains(".Set(LooseNote.Attrs.Owner, value) with the value the item holds, so that it writes gsi2pk too.", StringComparison.Ordinal);
    }

    private static StoredNote NewNote() => new() { Id = Id, Owner = Ann, LiveSince = Noon.AddDays(-1) };

    private static async Task SoftDeleteAsync(Table<StoredNote> notes, TimeProvider clock) =>
        await notes.Update(StoredNote.Keys.Of(Ann, Id)).Set(StoredNote.Attrs.DeletedAt, clock.GetUtcNow()).Remove(StoredNote.Attrs.LiveSince).If(StoredNote.Attrs.DeletedAt.NotExists()).SendAsync();

    private static async Task<List<StoredNote>> LiveNotesAsync(Table<StoredNote> notes) =>
        await notes.Index(StoredNote.Indexes.Gsi2).Query(StoredNote.Keys.Gsi2Pk.Of(Ann)).ItemsAsync().ToListAsync();
}

[DynamoDbEntity]
[Discriminator("NOTE")]
[PartitionKey("OWNER#{Owner}")]
[SortKey("NOTE#{Id}")]
[GlobalIndexKey("GSI2", PartitionKey = "OWNER#{Owner}", SortKey = "LIVE#{LiveSince}", Sparse = true)]
internal sealed partial record StoredNote
{
    public required string Owner { get; init; }

    public required string Id { get; init; }

    public DateTimeOffset? LiveSince { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }
}

[DynamoDbEntity]
[Discriminator("LOOSE_NOTE")]
[PartitionKey("NOTE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI2", PartitionKey = "OWNER#{Owner}", SortKey = "LIVE#{LiveSince}", Sparse = true)]
internal sealed partial record LooseNote
{
    public required string Id { get; init; }

    public required string Owner { get; init; }

    public DateTimeOffset? LiveSince { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }
}
