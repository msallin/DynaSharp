namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA215 on real snippets: a handle of one mapped type on a command of another, which DynamoDB answers by finding nothing.</summary>
public sealed class HandleOwnerAnalyzerTests
{
    private const string Model = """
        [DynamoDbEntity]
        [Discriminator("INVOICE")]
        [PartitionKey("USER#{UserId}")]
        public partial class Invoice
        {
            public required string UserId { get; init; }

            public decimal Total { get; init; }

            public string? Note { get; init; }

            public Address? ShipTo { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("SUBSCRIPTION")]
        [PartitionKey("USER#{UserId}")]
        public partial class Subscription
        {
            public required string UserId { get; init; }

            public int Seats { get; init; }

            public string? Plan { get; init; }

            public Address? ShipTo { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string City, Geo? Place);

        [DynamoDbDocument]
        public partial record Geo(string Name);

        [DynamoDbEntity]
        [Discriminator("BASE")]
        [PartitionKey("USER#{UserId}")]
        public partial class Base
        {
            public required string UserId { get; init; }

            public int Size { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("DERIVED")]
        [PartitionKey("USER#{UserId}")]
        public partial class Derived : Base
        {
            public string? Extra { get; init; }
        }
        """;

    private const string Fields = """
        private static readonly Table<Invoice> invoices = null!;
        private static readonly Table<Subscription> subscriptions = null!;
        private static readonly Table<Address> addresses = null!;
        private static readonly Table<Base> bases = null!;
        private static readonly Table<Derived> derived = null!;
        private static readonly Index<Invoice> byNote = null!;
        private static readonly DynamoDbClient client = null!;

        """;

    private const string Helpers = "public static class Mine { public static UpdateStep Step() => default; public static void Filter<T, C>(Attr<T, C> attribute) where C : IDynamoDbConverter<T> { } public static void Audited<T, C>(UpdateCommand<Invoice> command, Attr<T, C> attribute) where C : IDynamoDbConverter<T> { } }";

    private static readonly AnalyzerFixture Shared = new(Model);

    private static readonly AnalyzerFixture WithHelpers = new(Model + Helpers);

    [Test]
    [Arguments("""invoices.Scan().Filter([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Query(Invoice.Keys.Pk.Of("a")).Filter([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""byNote.Query(new PartitionKey<Invoice>("a")).Filter([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Query([Invoice.Keys.Pk.Of("a"), Invoice.Keys.Pk.Of("b")]).Filter([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).If([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).If(Invoice.Attrs.Total.Gt(1).And([|Subscription.Attrs.Seats|].Gt(1)));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).If($"{[|Subscription.Attrs.Seats|]} > {1}");""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Set([|Subscription.Attrs.Seats|], 1);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).SetIfNotExists([|Subscription.Attrs.Seats|], 1);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Add([|Subscription.Attrs.Seats|], 1);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Remove([|Subscription.Attrs.Seats|]);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Return([|Subscription.Attrs.Seats|]);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Put(null!).If([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Delete(Invoice.Keys.Of("a")).If([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Get(Invoice.Keys.Of("a")).Project([|Subscription.Attrs.Seats|]);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Get(Invoice.Keys.Of("a")).Select([|Subscription.Attrs.Seats|]);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Scan().Select([|Subscription.Attrs.Plan|]);""", "Subscription.Attrs.Plan", "Subscription", "Invoice")]
    [Arguments("""invoices.Query(Invoice.Keys.Pk.Of("a")).Project(Invoice.Attrs.Total, [|Subscription.Attrs.Seats|]);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Set([|Subscription.Attrs.ShipTo|].At(Address.Attrs.City), "x");""", "Subscription.Attrs.ShipTo", "Subscription", "Invoice")]
    [Arguments("""invoices.Scan().Filter([|Address.Attrs.City|].Eq("x"));""", "Address.Attrs.City", "Address", "Invoice")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Set(Invoice.Attrs.Note, "x").Remove([|Subscription.Attrs.Plan|]);""", "Subscription.Attrs.Plan", "Subscription", "Invoice")]
    [Arguments("""client.TransactWrite().Update(invoices, Invoice.Keys.Of("a")).Set([|Subscription.Attrs.Seats|], 1);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""client.TransactWrite().Update(invoices, Invoice.Keys.Of("a")).Add(Invoice.Attrs.Total, 1m).If([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""client.TransactWrite().Check(invoices, Invoice.Keys.Of("a")).If([|Subscription.Attrs.Seats|].Gt(1));""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    [Arguments("""client.TransactWrite().Put(subscriptions, null!).Update(invoices, Invoice.Keys.Of("a")).Set([|Subscription.Attrs.Seats|], 1);""", "Subscription.Attrs.Seats", "Subscription", "Invoice")]
    public async Task Analyze_HandleOfAnotherType_ReportsTheHandle(string statement, string handle, string owner, string command)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await Shared.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(handle, owner, command)));
    }

    [Test]
    public async Task Analyze_HandleOutsideAllTypesOfAMixedRead_NamesThemAll()
    {
        // Arrange
        var source = Source("""shop.Query("a").Of<Invoice, Base>().Filter([|Subscription.Attrs.Seats|].Gt(1));""");

        // Act
        var report = await Shared.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Subscription.Attrs.Seats", "Subscription", "Invoice, Base")));
    }

    [Test]
    [Arguments("""invoices.Scan().Filter(Invoice.Attrs.Total.Gt(1));""")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Set(Invoice.Attrs.Note, "x").Add(Invoice.Attrs.Total, 1m);""")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).If($"{Invoice.Attrs.Total} > {1}");""")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Set(Invoice.Attrs.ShipTo.At(Address.Attrs.City), "x");""")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).Set(Invoice.Attrs.ShipTo.At(Address.Attrs.Place).At(Geo.Attrs.Name), "x");""")]
    [Arguments("""invoices.Query(Invoice.Keys.Pk.Of("a")).Filter(Invoice.Attrs.ShipTo.At(Address.Attrs.City).Eq("x"));""")]
    [Arguments("""invoices.Update(Invoice.Keys.Of("a")).If($"{Invoice.Attrs.Note} = {Subscription.Attrs.Plan.Value("x")}");""")]
    [Arguments("""shop.Update("a").Set(Invoice.Attrs.Note, "x");""")]
    [Arguments("""shop.Scan().Filter(Invoice.Attrs.Total.Gt(1));""")]
    [Arguments("""shop.Query("a").Of<Invoice, Subscription>().Filter(Subscription.Attrs.Seats.Gt(1));""")]
    [Arguments("""shop.Query("a").Of<Invoice, Subscription>().Project(Invoice.Attrs.Total, Subscription.Attrs.Seats);""")]
    [Arguments("""shop.Scan().Of<Invoice, Subscription>().Filter(Invoice.Attrs.Total.Gt(1));""")]
    [Arguments("""derived.Scan().Filter(Base.Attrs.Size.Gt(1));""")]
    [Arguments("""bases.Scan().Filter(Derived.Attrs.Extra.Eq("x"));""")]
    [Arguments("""addresses.Scan().Filter(Address.Attrs.City.Eq("x"));""")]
    [Arguments("""invoices.Scan().Filter(Attr("pk").Gt(1));""")]
    [Arguments("""var seats = Subscription.Attrs.Seats; invoices.Scan().Filter(seats.Gt(1));""")]
    [Arguments("""Mine.Filter(Subscription.Attrs.Seats);""")]
    [Arguments("""Mine.Audited(invoices.Update(Invoice.Keys.Of("a")), Subscription.Attrs.Seats);""")]
    [Arguments("""var update = invoices.Update(Invoice.Keys.Of("a")); client.TransactWrite().Update(invoices, Invoice.Keys.Of("a")).Set(Invoice.Attrs.Note, "x");""")]
    [Arguments("""UpdateStep step = default; step.Set(Subscription.Attrs.Seats, 1);""")]
    [Arguments("""Mine.Step().Set(Subscription.Attrs.Seats, 1);""")]
    [Arguments("""Subscription.Attrs.Seats.Gt(1).And(Subscription.Attrs.Plan.Eq("x"));""")]
    [Arguments("""client.TransactWrite().Update(invoices, Invoice.Keys.Of("a")).Set(Invoice.Attrs.Note, "x").Update(subscriptions, Subscription.Keys.Of("a")).Set(Subscription.Attrs.Seats, 1);""")]
    public async Task Analyze_HandleThatFits_ReportsNothing(string statement)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await WithHelpers.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_HelperOverACommandOfAnyType_ReportsNothing()
    {
        // Arrange (what a generic helper runs on is not known here)
        var source = AnalyzerRun.Member(
            """
            public static UpdateCommand<T> Touch<T>(UpdateCommand<T> command) where T : IDynamoDbDocument<T> => command.Set(Subscription.Attrs.Plan, "x");
            """);

        // Act
        var report = await Shared.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Source(string statement) =>
        AnalyzerRun.Statements(statement).Replace("public static void Run()", Fields + "public static void Run()", StringComparison.Ordinal);

    private static string Message(string handle, string owner, string command) =>
        $"DYNA215 Warning: '{handle}' is an attribute of '{owner}', and this command is on '{command}', so DynamoDB looks for it on items that do not have it. Use a handle of that type, or a raw path for an attribute outside it.";
}
