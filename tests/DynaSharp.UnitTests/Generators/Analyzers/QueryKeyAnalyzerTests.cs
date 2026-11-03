using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA221, DYNA222 and DYNA224: what a query names against the keys of what it reads.</summary>
public sealed class QueryKeyAnalyzerTests
{
    private const string Entities = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        [SortKey("ORDER#{PlacedAt:ticks}#{OrderId}")]
        [GlobalIndexKey("ByMarket", PartitionKey = "MARKET#{Market}#{Channel}", SortKey = "{PlacedOn:yyyy-MM}#{OrderId}")]
        [GlobalIndexKey("ByUser", PartitionKey = "USER#{UserId}", SortKey = "ORDER#{PlacedAt:ticks}#{OrderId}")]
        [GlobalIndexKey("ByReference", PartitionKey = "{Reference}", PartitionKeyAttributeName = "Reference")]
        [LocalIndexKey("ByTotal", "{Total:n10.2}")]
        public partial class Order
        {
            public required string UserId { get; init; }

            public required DateTimeOffset PlacedAt { get; init; }

            public required string OrderId { get; init; }

            public required string Market { get; init; }

            public required string Channel { get; init; }

            public required DateTimeOffset PlacedOn { get; init; }

            public required string Reference { get; init; }

            public decimal Total { get; init; }

            public string? Note { get; init; }

            public Address? ShipTo { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string UserId);

        [DynamoDbEntity]
        [Discriminator("TICKET")]
        public partial class Ticket
        {
            [PartitionKey]
            public required string Code { get; init; }

            [SortKey]
            public required int Seq { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("SUMMARY")]
        [PartitionKey("MONTH#{Month:yyyy-MM}")]
        public partial class Summary
        {
            public required DateTimeOffset Month { get; init; }

            public decimal Sum { get; init; }
        }

        public static class Fake
        {
            public static class Keys
            {
                public static class Pk
                {
                    public static PartitionKey<Order> Of() => default;
                }
            }
        }

        public static class Keys
        {
            public static class Pk
            {
                public static PartitionKey<Order> Of() => default;
            }
        }

        [DynamoDbDocument]
        public partial record Part(string Name)
        {
            public static class Keys
            {
                public static class Pk
                {
                    public static PartitionKey<Order> Of() => default;
                }
            }
        }
        """;

    private const string Fields = """
        private static readonly Table<Order> orders = null!;
        private static readonly Table<Ticket> tickets = null!;
        private static readonly Table<Summary> summaries = null!;
        private static readonly Index<Order> byMarket = null!;
        private static readonly DateTimeOffset now = default;
        private static string Name() => "ByMarket";

        """;

    private const string Missed = ", and the filter drops every item that an Update created, which has the key and no attribute of its own for it";

    private const string Rejected = ", and DynamoDB rejects a filter on a key of what it reads";

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""orders.Query(Order.Keys.Pk.Of("u")).Filter([|Order.Attrs.UserId|].Eq("u"))""", "UserId", "pk", Missed)]
    [Arguments("""orders.Query(Order.Keys.Pk.Of("u")).Limit(1).Filter([|Order.Attrs.PlacedAt|].Gt(now))""", "PlacedAt", "sk", Missed)]
    [Arguments("""orders.Query("u").Filter($"{[|Order.Attrs.UserId|]} = {"u"}")""", "UserId", "pk", Missed)]
    [Arguments("""orders.Query("u").Filter(Attr([|"UserId"|]).Eq("u"))""", "UserId", "pk", Missed)]
    [Arguments("""orders.Query("u").Filter(Attr([|"pk"|]).Exists())""", "pk", "pk", Rejected)]
    [Arguments("""orders.Query(["u", "v"]).Filter([|Order.Attrs.UserId|].Eq("u"))""", "UserId", "pk", Missed)]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query("m").Filter([|Order.Attrs.Channel|].Eq("web"))""", "Channel", "bymarketpk", "")]
    [Arguments("""orders.Index("ByMarket", "bymarketpk", "bymarketsk").Query("m").Filter([|Order.Attrs.Market|].Eq("eu"))""", "Market", "bymarketpk", "")]
    [Arguments("""orders.Index(Order.Indexes.ByReference).Query("r").Filter([|Order.Attrs.Reference|].Eq("r"))""", "Reference", "Reference", Rejected)]
    [Arguments("""tickets.Query("c").Filter([|Ticket.Attrs.Seq|].Gt(1))""", "Seq", "Seq", Rejected)]
    [Arguments("""tickets.Query("c").Filter([|Ticket.Attrs.Code|].Eq("c"))""", "Code", "Code", Rejected)]
    [Arguments("""orders.Query("u").Filter([|Order.Attrs.UserId|].Eq("a").Or(Order.Attrs.UserId.Eq("b")))""", "UserId", "pk", Missed)]
    public async Task Analyze_FilterOnWhatTheKeyConditionDecides_Warns(string chain, string name, string key, string why)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new QueryKeyAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA221 Warning: '{name}' is held by the key '{key}' that this query reads, so the key condition decides what this filter tests{why}. Test it there."));
    }

    [Test]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query("m").Filter([|Order.Attrs.PlacedOn|].Gt(now))""", "PlacedOn", "bymarketsk")]
    [Arguments("""summaries.Query("MONTH#2026-01").Filter([|Summary.Attrs.Month|].Gt(now))""", "Month", "pk")]
    public async Task Analyze_FilterOnWhatAKeyHoldsInPart_IsInfo(string chain, string name, string key)
    {
        // Arrange (a month in the key and a day in the filter: the filter is finer than the key, which is correct)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new QueryKeyAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA222 Info: '{name}' is held in part by the key '{key}' that this query reads, so the key condition can narrow the read before this filter. A filter finer than the key is fine."));
    }

    [Test]
    [Arguments("""orders.Query(Order.Keys.Pk.Of("u")).Filter(Order.Attrs.Total.Gt(1m))""")]
    [Arguments("""orders.Query(Order.Keys.Pk.Of("u")).Filter(Order.Attrs.OrderId.Eq("o"))""")]
    [Arguments("""orders.Query("u").Filter(Order.Attrs.ShipTo.At(Address.Attrs.UserId).Eq("u"))""")]
    [Arguments("""orders.Query("u").Filter(Attr("Note").Exists())""")]
    [Arguments("""orders.Query("u").Filter(Attr("nothing").Exists())""")]
    [Arguments("""orders.Query("u").Filter(Attr("a..b").Exists())""")]
    [Arguments("""orders.Query("u").Filter(Attr<int>("Total").Eq(1).And(new Attr("Note").Exists()))""")]
    [Arguments("""orders.Scan().Filter(Order.Attrs.UserId.Eq("u"))""")]
    [Arguments("""byMarket.Query("m").Filter(Order.Attrs.Market.Eq("eu"))""")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query("m").Filter(Order.Attrs.UserId.Eq("u"))""")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query("m").Filter(Order.Attrs.OrderId.Eq("o"))""")]
    [Arguments("""orders.Index("Nope").Query("m").Filter(Order.Attrs.Market.Eq("eu"))""")]
    [Arguments("""orders.Index(Name()).Query("m").Filter(Order.Attrs.Market.Eq("eu"))""")]
    [Arguments("""orders.Index("ByMarket", Name()).Query("m").Filter(Order.Attrs.Market.Eq("eu"))""")]
    [Arguments("""orders.Index("ByMarket", "bymarketpk", Name()).Query("m").Filter(Order.Attrs.Market.Eq("eu"))""")]
    [Arguments("""orders.Index("ByMarket", "bymarketpk").Query("m").Filter(Order.Attrs.PlacedOn.Gt(now))""")]
    [Arguments("""orders.Index("Other", "otherpk", "othersk").Query("m").Filter(Order.Attrs.Market.Eq("eu"))""")]
    [Arguments("""shop.Query("a").Filter(Attr("pk").Exists())""")]
    [Arguments("""shop.Query("a").Of<Order, Ticket>().Filter(Order.Attrs.UserId.Eq("u"))""")]
    [Arguments("""default(QueryCommand<Order>).Filter(Order.Attrs.UserId.Eq("u"))""")]
    public async Task Analyze_FilterTheKeyConditionCannotSayOrThatCannotBeFollowed_ReportsNothing(string chain)
    {
        // Arrange (a later placeholder of the sort key, a key of the table on an index, an index that cannot be known, and a chain that does not start at its verb)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new QueryKeyAnalyzer());

        // Assert
        await Assert.That(Shared.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_FilterOfALaterPlaceholderOfTheSortKey_IsTheBitemporalRecipe()
    {
        // Arrange (when a fact was recorded is the second time in the key: no key condition can test it alone, and the items come from Put)
        var source = AnalyzerRun.Member(Fields + """public static void Run() { _ = orders.Query(Order.Keys.Pk.Of("u"), Order.Keys.Sk.Le(now)).Filter(Order.Attrs.OrderId.Eq("o")).Descending(); }""");

        // Act
        var report = await Shared.ReportWithAsync(source, new QueryKeyAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query([|Order.Keys.Pk.Of("u")|])""", "Order.Keys.Pk", "pk", "the index ByMarket", "bymarketpk", "it throws ArgumentException. Use Order.Keys.ByMarketPk.")]
    [Arguments("""orders.Index(Order.Indexes.ByUser).Query([|Order.Keys.Pk.Of("u")|], Order.Keys.Sk.BeginsWith())""", "Order.Keys.Pk", "pk", "the index ByUser", "byuserpk", "it throws ArgumentException. Use Order.Keys.ByUserPk.")]
    [Arguments("""orders.Query([|Order.Keys.ByMarketPk.Of("eu", "web")|])""", "Order.Keys.ByMarketPk", "bymarketpk", "the table", "pk", "it throws ArgumentException. Use Order.Keys.Pk.")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query(new[] { "u" }.Select([|Order.Keys.Pk.Of|]))""", "Order.Keys.Pk", "pk", "the index ByMarket", "bymarketpk", "it throws ArgumentException. Use Order.Keys.ByMarketPk.")]
    [Arguments("""orders.Index("Other", "otherpk").Query([|Order.Keys.Pk.Of("u")|])""", "Order.Keys.Pk", "pk", "the index Other", "otherpk", "it throws ArgumentException.")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query(Order.Keys.ByMarketPk.Of("eu", "web"), [|Order.Keys.Sk.BeginsWith()|])""", "Order.Keys.Sk", "sk", "the index ByMarket", "bymarketsk", "DynamoDB compares that attribute with text made for another key and finds the wrong items. Use Order.Keys.ByMarketSk.")]
    [Arguments("""orders.Index("Other", "otherpk", "othersk").Query("o", [|Order.Keys.Sk.BeginsWith()|])""", "Order.Keys.Sk", "sk", "the index Other", "othersk", "DynamoDB compares that attribute with text made for another key and finds the wrong items.")]
    [Arguments("""orders.Index(Order.Indexes.ByReference).Query("r", [|Order.Keys.Sk.BeginsWith()|])""", "Order.Keys.Sk", "sk", "the index ByReference", "no sort key", "it throws ArgumentException. Leave out the condition on the sort key.")]
    public async Task Analyze_KeyOfAnotherAttribute_Warns(string chain, string builder, string attribute, string read, string reads, string reason)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new QueryKeyAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA224 Warning: {builder} builds a key of the attribute {attribute}, and this query of {read} reads {reads}, so {reason}"));
    }

    [Test]
    [Arguments("""orders.Query(Order.Keys.Pk.Of("u"), Order.Keys.Sk.BeginsWith())""")]
    [Arguments("""orders.Index(Order.Indexes.ByUser).Query(Order.Keys.ByUserPk.Of("u"), Order.Keys.Sk.BeginsWith())""")]
    [Arguments("""orders.Index(Order.Indexes.ByUser).Query(Order.Keys.ByUserPk.Of("u"), Order.Keys.ByUserSk.BeginsWith())""")]
    [Arguments("""orders.Index(Order.Indexes.ByTotal).Query(Order.Keys.Pk.Of("u"), Order.Keys.ByTotalSk.Gt(1m))""")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query(Order.Keys.Pk.Format("u"))""")]
    [Arguments("""orders.Index("Other", "pk", "sk").Query(Order.Keys.Pk.Of("u"), Order.Keys.Sk.BeginsWith())""")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query(Fake.Keys.Pk.Of())""")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query(Keys.Pk.Of())""")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query(Part.Keys.Pk.Of())""")]
    [Arguments("""byMarket.Query(Order.Keys.Pk.Of("u"))""")]
    [Arguments("""shop.Query(Order.Keys.Pk.Of("u"))""")]
    [Arguments("""orders.Index(Order.Indexes.ByMarket).Query("m", default)""")]
    public async Task Analyze_KeyOfTheAttributeThatIsReadOrThatCannotBeKnown_ReportsNothing(string chain)
    {
        // Arrange (an index whose sort key has the template of the table under another attribute takes the same text, and a local index has the partition key of the table)
        var source = AnalyzerRun.Member(Fields + "public static void Run() { _ = " + chain + "; }");

        // Act
        var report = await Shared.ReportWithAsync(source, new QueryKeyAnalyzer());

        // Assert
        await Assert.That(Shared.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_StaticMethodUnderTheNamespaceOfTheLibrary_HasNoTable()
    {
        // Arrange (a helper of the consumer in DynaSharp.Extras counts as the library and has no receiver)
        var source = """
            using DynaSharp;
            using DynaSharp.Mapping;

            namespace DynaSharp.Extras
            {
                [DynamoDbEntity]
                [Discriminator("NOTE")]
                [PartitionKey("NOTE#{Id}")]
                public partial class Note
                {
                    public required string Id { get; init; }
                }

                public static class Helpers
                {
                    public static QueryCommand<Note> Query() => default;

                    public static QueryCommand<Note> Start() => default;

                    public static void Run()
                    {
                        _ = Query().Filter(Note.Attrs.Id.Eq("a"));
                        _ = Start().Filter(Note.Attrs.Id.Eq("a"));
                    }
                }
            }
            """;

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new QueryKeyAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }
}
