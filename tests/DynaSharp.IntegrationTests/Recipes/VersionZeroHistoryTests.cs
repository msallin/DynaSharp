using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Version-0 item (history)</see>: the current state is the item at version 0, and each change also writes an immutable item of its own. A transaction keeps the two together, with the
/// version check on the current item deciding between writers. It costs twice: two items, each at the price of a transactional write.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class VersionZeroHistoryTests(Backend backend)
{
    private const string Terms = "terms";

    [Test]
    public async Task SaveAsync_TwoChanges_KeepTheCurrentStateAndEveryChange()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await SaveAsync(sandbox, new Policy { Id = Terms, Text = "first" });
        await SaveAsync(sandbox, (await ReadAsync(sandbox)) with { Text = "second" });

        // Act
        var current = await ReadAsync(sandbox);
        var history = await sandbox.Shop.Of<PolicyVersion>().Query(PolicyVersion.Keys.Pk.Of(Terms), PolicyVersion.Keys.Sk.BeginsWith()).Descending().Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((current.Text, current.Revision, string.Join(",", history.Select(static version => $"{version.Revision}:{version.Text}")))).IsEqualTo(("second", 2L, "2:second,1:first"));
    }

    [Test]
    public async Task SaveAsync_StaleWriter_WritesNeitherItem()
    {
        // Arrange (two writers hold version 1, and the other one saves first)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await SaveAsync(sandbox, new Policy { Id = Terms, Text = "first" });
        var held = await ReadAsync(sandbox);
        await SaveAsync(sandbox, held with { Text = "other" });

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(async () => await SaveAsync(sandbox, held with { Text = "mine" }));

        // Assert
        var history = await sandbox.Shop.Of<PolicyVersion>().Query(PolicyVersion.Keys.Pk.Of(Terms), PolicyVersion.Keys.Sk.BeginsWith()).Consistent().CountAsync();
        await Assert.That((failure.Failures[0].Index, (await ReadAsync(sandbox)).Text, history)).IsEqualTo((0, "other", 2));
    }

    [Test]
    public async Task SaveAsync_OneChange_CostsTwoTransactionalWrites()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.TransactionCapacityPerItem);
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var result = await SaveAsync(sandbox, new Policy { Id = Terms, Text = "first" });

        // Assert (two items, at 2 units each)
        await Assert.That(result.ConsumedCapacity.Sum(static capacity => capacity.CapacityUnits)).IsEqualTo(4.0);
    }

    private static async Task<Policy> ReadAsync(CommandSandbox sandbox) => (await sandbox.Shop.Of<Policy>().Get(Policy.Keys.Of(Terms)).Consistent().ItemAsync())!;

    // A put holds the version it read, so the transaction fails for a stale writer, and the history item can then not exist either.
    private static async Task<TransactWriteResult> SaveAsync(CommandSandbox sandbox, Policy policy) =>
        await sandbox.Client.TransactWrite()
            .Put(sandbox.Shop.Of<Policy>(), policy)
            .Put(sandbox.Shop.Of<PolicyVersion>(), new PolicyVersion { Id = policy.Id, Revision = policy.Revision + 1, Text = policy.Text }).IfNotExists()
            .ReturnCapacity()
            .SendAsync();
}

/// <summary>The current state, at the sort key that all history reads skip.</summary>
[DynamoDbEntity]
[Discriminator("POLICY")]
[PartitionKey("POLICY#{Id}")]
[SortKey("v0")]
internal sealed partial record Policy
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    [Version]
    public long Revision { get; init; }
}

[DynamoDbEntity]
[Discriminator("POLICY_VERSION")]
[PartitionKey("POLICY#{Id}")]
[SortKey("v#{Revision:d6}")]
internal sealed partial record PolicyVersion
{
    public required string Id { get; init; }

    public required long Revision { get; init; }

    public required string Text { get; init; }
}
