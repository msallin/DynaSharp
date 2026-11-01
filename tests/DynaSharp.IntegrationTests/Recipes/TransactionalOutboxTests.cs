using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Transactional outbox</see>: a state change and the event about it are written in one transaction, so neither happens without the other, and a relay publishes what it finds and deletes it. It
/// delivers at least once, so a consumer goes by the event id. Often the stream of the table already is this outbox, which saves the item and the relay.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TransactionalOutboxTests(Backend backend)
{
    private static ManualClock NewClock => new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task PayAsync_Succeeds_WritesThePaymentAndItsEventTogether()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await PayAsync(sandbox, NewClock, "p1");

        // Assert
        var paid = await sandbox.Shop.Of<Payment>().Get(Payment.Keys.Of("p1")).Consistent().SendAsync();
        await Assert.That((paid.Found, (await PendingAsync(sandbox)).Single().PaymentId)).IsEqualTo((true, "p1"));
    }

    [Test]
    public async Task PayAsync_ConditionFails_WritesNoEvent()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Payment>().Put(new Payment { Id = "p1" }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => PayAsync(sandbox, NewClock, "p1"));

        // Assert
        await Assert.That((failure.Failures[0].Reason, (await PendingAsync(sandbox)).Count)).IsEqualTo((TransactionFailure.ConditionalCheckFailed, 0));
    }

    [Test]
    public async Task RelayAsync_DeliversInOrderAndEmptiesTheOutbox()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var clock = NewClock;
        await PayAsync(sandbox, clock, "p1");
        clock.Advance(TimeSpan.FromMinutes(1));
        await PayAsync(sandbox, clock, "p2");
        var published = new List<string>();

        // Act
        await RelayAsync(sandbox, published.Add);

        // Assert
        await Assert.That((string.Join(",", published), (await PendingAsync(sandbox)).Count)).IsEqualTo(("p1,p2", 0));
    }

    [Test]
    public async Task RelayAsync_CrashAfterThePublish_DeliversTheSameEventIdAgain()
    {
        // Arrange (published, and the relay died before its delete)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await PayAsync(sandbox, NewClock, "p1");
        var crashed = (await PendingAsync(sandbox)).Single();
        var published = new List<string>();

        // Act
        await RelayAsync(sandbox, published.Add);

        // Assert
        await Assert.That(((await PendingAsync(sandbox)).Count, published.Single())).IsEqualTo((0, crashed.PaymentId));
    }

    private static async Task PayAsync(CommandSandbox sandbox, TimeProvider clock, string id) =>
        await sandbox.Client.TransactWrite()
            .Put(sandbox.Shop.Of<Payment>(), new Payment { Id = id }).IfNotExists()
            .Put(sandbox.Shop.Of<OutboxEvent>(), new OutboxEvent { At = clock.GetUtcNow(), EventId = Guid.NewGuid(), PaymentId = id })
            .SendAsync();

    private static async Task<List<OutboxEvent>> PendingAsync(CommandSandbox sandbox) =>
        await sandbox.Shop.Of<OutboxEvent>().Query(OutboxEvent.Keys.Pk.Of()).Consistent().ItemsAsync().ToListAsync();

    private static async Task RelayAsync(CommandSandbox sandbox, Action<string> publish)
    {
        foreach (var pending in await PendingAsync(sandbox))
        {
            publish(pending.PaymentId);
            await sandbox.Shop.Of<OutboxEvent>().Delete(pending).SendAsync();
        }
    }
}

[DynamoDbEntity]
[Discriminator("PAYMENT")]
[PartitionKey("PAYMENT#{Id}")]
[SortKey("STATE")]
internal sealed partial record Payment
{
    public required string Id { get; init; }
}

[DynamoDbEntity]
[Discriminator("OUTBOX")]
[PartitionKey("OUTBOX")]
[SortKey("{At}#{EventId}")]
internal sealed partial record OutboxEvent
{
    public required DateTimeOffset At { get; init; }

    public required Guid EventId { get; init; }

    public required string PaymentId { get; init; }
}
