using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Optimistic locking</see>: a read-modify-write stays correct without a lock, because the write holds only if nobody wrote since the read. <c>[Version]</c> turns it on per
/// type and every write of that type must then say how it treats the version. What to do about a conflict is up to the caller: nothing retries by itself.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class OptimisticLockingTests(Backend backend)
{
    private const string Alice = "alice";

    // A failed condition means that another writer got through, so no writer can lose more often than there are others.
    private const int Writers = 8;

    [Test]
    public async Task Put_ConcurrentReadModifyWrites_LoseNoUpdate()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var accounts = sandbox.Shop.Of<Account>();
        await accounts.Put(new Account { Id = Alice }).SendAsync();

        // Act
        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => DepositAsync(accounts, Alice, 10m)));

        // Assert
        var account = await accounts.Get(Account.Keys.Of(Alice)).Consistent().ItemAsync();
        await Assert.That((account!.Balance, account.Revision)).IsEqualTo((80m, 9L));
    }

    [Test]
    public async Task Put_NoVersionOnTheType_LosesTheUpdateOfTheOtherWriter()
    {
        // Arrange (locking is opt-in per type, and this type did not: both read 0)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var wallets = sandbox.Shop.Of<Wallet>();
        var held = new Wallet { Id = Alice };
        await wallets.Put(held).SendAsync();

        // Act
        await wallets.Put(held with { Balance = held.Balance + 10m }).SendAsync();
        await wallets.Put(held with { Balance = held.Balance + 10m }).SendAsync();

        // Assert
        await Assert.That((await wallets.Get(Wallet.Keys.Of(Alice)).Consistent().ItemAsync())!.Balance).IsEqualTo(10m);
    }

    [Test]
    public async Task Put_ReturnNew_GivesTheItemAsARead_ForTheNextWrite()
    {
        // Arrange (the entity of the put keeps its version, the item of ReturnNew holds the stored one)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var accounts = sandbox.Shop.Of<Account>();
        var opened = new Account { Id = Alice };

        // Act
        var saved = (await accounts.Put(opened).ReturnNew().SendAsync()).Item!;
        var next = (await accounts.Put(saved with { Balance = 5m }).ReturnNew().SendAsync()).Item!;
        var read = await accounts.Get(Account.Keys.Of(Alice)).Consistent().ItemAsync();

        // Assert
        await Assert.That((opened.Revision, saved.Revision, read == next)).IsEqualTo((0L, 1L, true));
    }

    [Test]
    public async Task Update_NoWordOnTheVersion_ThrowsBeforeItIsSent()
    {
        // Arrange (enforced per write: a key does not carry a version, so the write has to say)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var accounts = sandbox.Shop.Of<Account>();

        // Act
#pragma warning disable DYNA239 // The write says nothing of the version on purpose.
        var failure = await Items.FailureOfAsync<InvalidOperationException>(async () => await accounts.Update(Account.Keys.Of(Alice)).Set(Account.Attrs.Balance, 1m).SendAsync());
#pragma warning restore DYNA239

        // Assert
        await Assert.That(failure.Message).Contains("IfVersion", StringComparison.Ordinal);
    }

    // Someone who writes since the read makes the deposit start over, from what is there now.
    private static Task DepositAsync(Table<Account> accounts, string id, decimal amount) =>
        accounts.RetryOnConflictAsync(
            async ct =>
            {
                var account = await accounts.Get(Account.Keys.Of(id)).Consistent().ItemAsync(ct);
                await accounts.Put(account! with { Balance = account.Balance + amount }).SendAsync(ct);
            },
            maxAttempts: Writers);
}

[DynamoDbEntity]
[Discriminator("ACCOUNT")]
[PartitionKey("ACCOUNT#{Id}")]
[SortKey("BALANCE")]
internal sealed partial record Account
{
    public required string Id { get; init; }

    public decimal Balance { get; init; }

    [Version]
    public long Revision { get; init; }
}

[DynamoDbEntity]
[Discriminator("WALLET")]
[PartitionKey("WALLET#{Id}")]
[SortKey("BALANCE")]
internal sealed partial record Wallet
{
    public required string Id { get; init; }

    public decimal Balance { get; init; }
}
