using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Composite / hierarchical sort keys</see>: one partition holds a hierarchy, and a <c>begins_with</c> on a prefix reads it at any depth. The key is text, so order is byte order:
/// numbers need a fixed width, which zero-padding gives only to non-negative ones, and times need UTC in one format.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class CompositeSortKeyTests(Backend backend)
{
    private const string Acme = "acme";

    [Test]
    public async Task Query_PrefixAtEachDepth_ReadsThatLevelOfTheHierarchy()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var staff = sandbox.Shop.Of<Employee>();
        var pk = Employee.Keys.Pk.Of(Acme);
        await staff.BatchWrite().PutAll(
        [
            new Employee { Org = Acme, Country = "CH", City = "Bern", Number = 1 },
            new Employee { Org = Acme, Country = "CH", City = "Zurich", Number = 2 },
            new Employee { Org = Acme, Country = "CH", City = "Zurich", Number = 10 },
            new Employee { Org = Acme, Country = "DE", City = "Bonn", Number = 3 },
        ]).SendAsync();

        // Act
        var company = await staff.Query(pk, Employee.Keys.Sk.BeginsWith()).CountAsync();
        var country = await staff.Query(pk, Employee.Keys.Sk.BeginsWith("CH")).CountAsync();
        var city = await staff.Query(pk, Employee.Keys.Sk.BeginsWith("CH", "Zurich")).ItemsAsync().ToListAsync();

        // Assert (10 after 2: the number is padded)
        await Assert.That((company, country, string.Join(",", city.Select(static employee => employee.Number)))).IsEqualTo((4, 3, "2,10"));
    }

    [Test]
    public async Task Format_NegativeNumberInAPaddedKey_Throws()
    {
        // Act
        var failure = await Assert.That(static () => Employee.Keys.Sk.Format("CH", "Bern", -1)).Throws<KeyFormatException>();

        // Assert
        await Assert.That(failure!.Message).Contains("Number", StringComparison.Ordinal);
    }

    [Test]
    public async Task Query_SignedFixedPointKeys_ReadInNumericOrder()
    {
        // Arrange (n4 has a sign and a width, for what padding cannot order: as text, -10 would come after -2)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var moves = sandbox.Shop.Of<StockMove>();
        await moves.BatchWrite().PutAll(from delta in new[] { 5, -2, 10, -10, 0 } select new StockMove { Sku = "KB", Delta = delta }).SendAsync();

        // Act
        var read = await moves.Query(StockMove.Keys.Pk.Of("KB")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", read.Select(static move => move.Delta))).IsEqualTo("-10,-2,0,5,10");
    }

    [Test]
    public async Task Query_TimesOfDifferentOffsets_ReadInOrderOfTheInstant()
    {
        // Arrange (by the digits on the wall the first of these is the later one, so a key must not hold local time)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var events = sandbox.Shop.Of<AuditEvent>();
        var inZurich = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(2));
        var inLondon = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await events.Put(new AuditEvent { Stream = "s", At = inLondon, What = "second" }).SendAsync();
        await events.Put(new AuditEvent { Stream = "s", At = inZurich, What = "first" }).SendAsync();

        // Act
        var read = await events.Query(AuditEvent.Keys.Pk.Of("s")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", read.Select(static auditEvent => auditEvent.What))).IsEqualTo("first,second");
    }
}

/// <summary>A value with a space sorts out of segment order, as DYNA115 says, and the order across cities does not matter here: <c>begins_with</c> on a whole field is not affected.</summary>
[DynamoDbEntity]
[Discriminator("EMPLOYEE")]
[PartitionKey("ORG#{Org}")]
[SortKey("SITE#{Country}#{City}#EMP#{Number:d6}")]
internal sealed partial record Employee
{
    public required string Org { get; init; }

    public required string Country { get; init; }

    public required string City { get; init; }

    public required int Number { get; init; }
}

[DynamoDbEntity]
[Discriminator("STOCK_MOVE")]
[PartitionKey("SKU#{Sku}")]
[SortKey("MOVE#{Delta:n4}")]
internal sealed partial record StockMove
{
    public required string Sku { get; init; }

    public required int Delta { get; init; }
}

[DynamoDbEntity]
[Discriminator("AUDIT")]
[PartitionKey("STREAM#{Stream}")]
[SortKey("AT#{At:ticks}")]
internal sealed partial record AuditEvent
{
    public required string Stream { get; init; }

    public required DateTimeOffset At { get; init; }

    public required string What { get; init; }
}
