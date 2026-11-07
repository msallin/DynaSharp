using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Gapless sequence</see>: numbers without holes, as invoices need. The counter moves in the same transaction as the record that takes the number, so a cancelled record burns none. It is
/// serialized on one item, so concurrent writers retry, and the throughput is that of the item.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class GaplessSequenceTests(Backend backend)
{
    private const string Series = "2026";
    private const string Good = "good";
    private const string Blocked = "blocked";

    // A failed condition means that another writer took the number, so no writer can lose more often than there are others.
    private const int Writers = 8;

    [Test]
    public async Task IssueAsync_ConcurrentWriters_TakeEveryNumberOnce()
    {
        // Arrange
        await using var sandbox = await StartAsync();

        // Act
        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => IssueAsync(sandbox, Good)));
        var invoices = await sandbox.Shop.Of<Invoice>().Query(Invoice.Keys.Pk.Of(Series), Invoice.Keys.Sk.BeginsWith()).Consistent().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", invoices.Select(static invoice => invoice.Number))).IsEqualTo("1,2,3,4,5,6,7,8");
    }

    [Test]
    public async Task IssueAsync_CancelledRecord_BurnsNoNumber()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        await IssueAsync(sandbox, Good);

        // Act
        var refused = await IssueAsync(sandbox, Blocked);
        var next = await IssueAsync(sandbox, Good);

        // Assert
        await Assert.That((refused, next)).IsEqualTo(((long?)null, (long?)2L));
    }

    private static Task<long?> IssueAsync(CommandSandbox sandbox, string debtor)
    {
        var counter = sandbox.Shop.Of<InvoiceSeries>();
        var key = InvoiceSeries.Keys.Of(Series);
        return counter.RetryOnConflictAsync<long?>(
            async ct =>
            {
                var last = (await counter.Get(key).Consistent().ItemAsync(ct))!.Last;
                TransactMarker refused = default;
                try
                {
                    await sandbox.Client.TransactWrite()
                        .Update(counter, key).Set(InvoiceSeries.Attrs.Last, last + 1).If(InvoiceSeries.Attrs.Last.Eq(last))
                        .Put(sandbox.Shop.Of<Invoice>(), new Invoice { Series = Series, Number = last + 1, Debtor = debtor }).IfNotExists()
                        .Check(sandbox.Shop.Of<Debtor>(), Debtor.Keys.Of(debtor), out refused).If(Debtor.Attrs.IsBlocked.Eq(false))
                        .SendAsync(ct);
                    return last + 1;
                }
                catch (TransactionCanceledException exception) when (exception.ReasonFor(refused) == TransactionFailure.ConditionalCheckFailed)
                {
                    // The run does not read the debtor, so a refusal has to end here. What leaves the delegate is another writer that took this number.
                    return null;
                }
            },
            maxAttempts: Writers);
    }

    private async Task<CommandSandbox> StartAsync()
    {
        var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<InvoiceSeries>().Put(new InvoiceSeries { Series = Series }).SendAsync();
        await sandbox.Shop.Of<Debtor>().Put(new Debtor { Id = Good }).SendAsync();
        await sandbox.Shop.Of<Debtor>().Put(new Debtor { Id = Blocked, IsBlocked = true }).SendAsync();
        return sandbox;
    }
}

[DynamoDbEntity]
[Discriminator("INVOICE_SERIES")]
[PartitionKey("SERIES#{Series}")]
[SortKey("COUNTER")]
internal sealed partial record InvoiceSeries
{
    public required string Series { get; init; }

    public long Last { get; init; }
}

[DynamoDbEntity]
[Discriminator("INVOICE")]
[PartitionKey("SERIES#{Series}")]
[SortKey("INVOICE#{Number:d8}")]
internal sealed partial record Invoice
{
    public required string Series { get; init; }

    public required long Number { get; init; }

    public required string Debtor { get; init; }
}

[DynamoDbEntity]
[Discriminator("DEBTOR")]
[PartitionKey("DEBTOR#{Id}")]
[SortKey("STATE")]
internal sealed partial record Debtor
{
    public required string Id { get; init; }

    public bool IsBlocked { get; init; }
}
