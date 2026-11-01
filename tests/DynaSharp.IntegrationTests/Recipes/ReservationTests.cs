using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Reservation with auto-release</see>: a seat is held for a few minutes while the customer pays. Nothing has to release it: the hold is a time in the item, a later condition treats a past time as free, and
/// paying is a transaction that holds only while the hold does.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ReservationTests(Backend backend)
{
    private const string Seat12 = "12A";

    private static readonly TimeSpan HoldTime = TimeSpan.FromMinutes(5);

    private static ManualClock NewClock => new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task TryHoldAsync_ConcurrentCustomers_LetsOneHold()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var clock = NewClock;

        // Act
        var held = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => TryHoldAsync(sandbox, clock, $"customer{i}")));

        // Assert
        await Assert.That(held.Count(static won => won)).IsEqualTo(1);
    }

    [Test]
    public async Task TryHoldAsync_AfterTheHoldRanOut_GetsTheSeatWithoutARelease()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var clock = NewClock;
        await TryHoldAsync(sandbox, clock, "ann");

        // Act
        var whileHeld = await TryHoldAsync(sandbox, clock, "ben");
        clock.Advance(HoldTime + TimeSpan.FromSeconds(1));
        var afterwards = await TryHoldAsync(sandbox, clock, "ben");

        // Assert
        await Assert.That((whileHeld, afterwards)).IsEqualTo((false, true));
    }

    [Test]
    public async Task ConfirmAsync_WhileHeld_SellsTheSeatForGood()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var clock = NewClock;
        await TryHoldAsync(sandbox, clock, "ann");

        // Act
        await ConfirmAsync(sandbox, clock, "ann");
        clock.Advance(HoldTime + TimeSpan.FromSeconds(1));
        var resale = await TryHoldAsync(sandbox, clock, "ben");

        // Assert
        var ticket = await sandbox.Shop.Of<SeatTicket>().Get(SeatTicket.Keys.Of(Seat12)).Consistent().ItemAsync();
        await Assert.That((ticket?.Customer, resale)).IsEqualTo(("ann", false));
    }

    [Test]
    public async Task ConfirmAsync_AfterTheHoldRanOut_CancelsAndWritesNoTicket()
    {
        // Arrange
        await using var sandbox = await StartAsync();
        var clock = NewClock;
        await TryHoldAsync(sandbox, clock, "ann");
        clock.Advance(HoldTime + TimeSpan.FromSeconds(1));

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => ConfirmAsync(sandbox, clock, "ann"));

        // Assert
        var ticket = await sandbox.Shop.Of<SeatTicket>().Get(SeatTicket.Keys.Of(Seat12)).Consistent().SendAsync();
        await Assert.That((failure.Failures.Single().Reason, ticket.Found)).IsEqualTo((TransactionFailure.ConditionalCheckFailed, false));
    }

    private static async Task<bool> TryHoldAsync(CommandSandbox sandbox, TimeProvider clock, string customer)
    {
        var now = clock.GetUtcNow();
        try
        {
            await sandbox.Shop.Of<Seat>().Update(Seat.Keys.Of(Seat12))
                .Set(Seat.Attrs.HeldBy, customer)
                .Set(Seat.Attrs.HeldUntil, now + HoldTime)
                .If(Seat.Attrs.SoldTo.NotExists().And(Seat.Attrs.HeldUntil.NotExists().Or(Seat.Attrs.HeldUntil.Le(now))))
                .SendAsync();
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static async Task ConfirmAsync(CommandSandbox sandbox, TimeProvider clock, string customer) =>
        await sandbox.Client.TransactWrite()
            .Update(sandbox.Shop.Of<Seat>(), Seat.Keys.Of(Seat12)).Set(Seat.Attrs.SoldTo, customer).If(Seat.Attrs.HeldBy.Eq(customer).And(Seat.Attrs.HeldUntil.Gt(clock.GetUtcNow())))
            .Put(sandbox.Shop.Of<SeatTicket>(), new SeatTicket { SeatNumber = Seat12, Customer = customer })
            .SendAsync();

    private async Task<CommandSandbox> StartAsync()
    {
        var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Seat>().Put(new Seat { Number = Seat12 }).SendAsync();
        return sandbox;
    }
}

[DynamoDbEntity]
[Discriminator("SEAT")]
[PartitionKey("SEAT#{Number}")]
[SortKey("STATE")]
internal sealed partial record Seat
{
    public required string Number { get; init; }

    public string? HeldBy { get; init; }

    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? HeldUntil { get; init; }

    public string? SoldTo { get; init; }
}

[DynamoDbEntity]
[Discriminator("SEAT_TICKET")]
[PartitionKey("SEAT#{SeatNumber}")]
[SortKey("TICKET")]
internal sealed partial record SeatTicket
{
    public required string SeatNumber { get; init; }

    public required string Customer { get; init; }
}
