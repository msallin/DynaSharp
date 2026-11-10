using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA241: an update by key that can create an item without the members a read of the entity requires.</summary>
public sealed class UnreadableUpdateAnalyzerTests
{
    private const string Entities = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        [SortKey("ORDER#{OrderId}")]
        public partial class Order
        {
            public required string UserId { get; init; }

            public required string OrderId { get; init; }

            public required string Status { get; init; }

            public required decimal Total { get; init; }

            [DynamoDbAttribute("market")]
            public required string Market { get; init; }

            public string? Note { get; init; }

            public int Count { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("COUNTER")]
        [PartitionKey("COUNTER#{Name}")]
        public partial class Counter
        {
            public required string Name { get; init; }

            public long Count { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("TALLY")]
        [PartitionKey("MONTH#{Month:yyyy-MM}")]
        public partial class Tally
        {
            public required DateTimeOffset Month { get; init; }

            public long Count { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("CART")]
        [PartitionKey("CART#{Id}")]
        public partial record Cart
        {
            public required string Id { get; init; }

            public required string Owner { get; init; }

            [Version]
            public long Revision { get; init; }
        }

        public sealed class Hand : IDynamoDbEntity<Hand>
        {
            static DynamoDbEntityInfo IDynamoDbEntity<Hand>.Info => null!;

            static void IDynamoDbEntity<Hand>.WriteKey(DynamoDbWriter writer, Hand entity) { }

            static void IDynamoDbDocument<Hand>.WriteAttributes(DynamoDbWriter writer, Hand entity) { }

            static Hand IDynamoDbDocument<Hand>.ReadAttributes(ref DynamoDbReader reader) => new();

            public readonly struct Key : IKey<Hand>
            {
                public void WriteKey(DynamoDbWriter writer) { }
            }
        }
        """;

    private const string Fields = """
        private static readonly DynamoDbClient client = null!;
        private static readonly Table<Order> orders = null!;
        private static readonly Table<Counter> counters = null!;
        private static readonly Table<Tally> tallies = null!;
        private static readonly Table<Cart> carts = null!;
        private static readonly Table<Hand> hands = null!;
        private static readonly Order order = null!;
        private static readonly Order.Keys.PrimaryKey key = default;
        private static readonly Cart.Keys.PrimaryKey cartKey = default;
        private static readonly DateTimeOffset now = DateTimeOffset.UnixEpoch;
        private static readonly long version = 3;

        """;

    private const string Outside = """

        public static class Steps
        {
            public static UpdateStep Touched(this UpdateStep step) => step;
        }

        public static class Mine
        {
            public static void Update() { }
        }

        """;

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""_ = orders.[|Update|](key).Set(Order.Attrs.Status, "paid").Set(Order.Attrs.Total, 1m).ToRequestJson();""", "'Market'", "Order")]
    [Arguments("""_ = orders.[|Update|](key).Set(Order.Attrs.Status, "paid").ToRequestJson();""", "'Total' and 'Market'", "Order")]
    [Arguments("""_ = orders.[|Update|](key).Set(Order.Attrs.Note, "x").Add(Order.Attrs.Count, 1).ToRequestJson();""", "'Status', 'Total' and 'Market'", "Order")]
    [Arguments("""_ = orders.[|Update|](key).Set("Status", "paid").Set("market", "eu").Set("a..b", 1).Set("pk", "x").ToRequestJson();""", "'Total'", "Order")]
    [Arguments("""_ = orders.[|Update|](key).Set($"{Order.Attrs.Total} = {1} + {Attr("Count")}").ToRequestJson();""", "'Status' and 'Market'", "Order")]
    [Arguments("""_ = orders.[|Update|](key).Remove(Order.Attrs.Status).ToRequestJson();""", "'Status', 'Total' and 'Market'", "Order")]
    [Arguments("""_ = tallies.[|Update|](Tally.Keys.Of(now)).Add(Tally.Attrs.Count, 1).ToRequestJson();""", "'Month'", "Tally")]
    [Arguments("""_ = client.TransactWrite().[|Update|](orders, key).Set(Order.Attrs.Status, "paid").Set(Order.Attrs.Total, 1m).ToRequestJson();""", "'Market'", "Order")]
    [Arguments("""_ = client.TransactWrite().[|Update|](orders, key).Set(Order.Attrs.Status, "paid").Set(Order.Attrs.Total, 1m).Put(orders, order).IfExists().ToRequestJson();""", "'Market'", "Order")]
    public async Task Analyze_UpdateThatCanCreateAnItemWithoutWhatAReadRequires_Warns(string statement, string missing, string entity)
    {
        // Arrange (a write names a member by its handle, by a constant path and in the text; a key input that the key holds in part is not given back by a read)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(missing, entity)));
    }

    [Test]
    [Arguments("""_ = orders.[|Update|](key).IfNotExists().Set(Order.Attrs.Note, "x").ToRequestJson();""", "'Status', 'Total' and 'Market'", "Order")]
    [Arguments("""_ = carts.[|Update|](cartKey).IfVersion(0).ToRequestJson();""", "'Owner'", "Cart")]
    public async Task Analyze_UpdateThatCreatesTheItemOnPurpose_StillWarns(string statement, string missing, string entity)
    {
        // Arrange (IfNotExists and version 0 stand for an item that does not exist, so the item is created without the members)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(missing, entity)));
    }

    [Test]
    [Arguments("""_ = orders.Update(key).Set(Order.Attrs.Note, "x").IfExists().ToRequestJson();""")]
    [Arguments("""_ = orders.Update(key).Set(Order.Attrs.Note, "x").If(Order.Attrs.Status.Exists()).ToRequestJson();""")]
    [Arguments("""_ = carts.Update(cartKey).IfVersion(3).ToRequestJson();""")]
    [Arguments("""_ = carts.Update(cartKey).IfVersion(version).ToRequestJson();""")]
    [Arguments("""_ = orders.Update(key).Set(Order.Attrs.Status, "x").SetIfNotExists(Order.Attrs.Total, 0m).SetIfNotNull(Order.Attrs.Market, "eu").ToRequestJson();""")]
    [Arguments("""_ = orders.Update(key).Set($"{Order.Attrs.Status} = {"x"}, {Attr("Total")} = {1}").Set("market", "eu").ToRequestJson();""")]
    [Arguments("""var update = orders.Update(key).Set(Order.Attrs.Note, "x"); _ = update.IfExists().ToRequestJson();""")]
    [Arguments("""_ = counters.Update(Counter.Keys.Of("c")).Add(Counter.Attrs.Count, 1).ToRequestJson();""")]
    [Arguments("""_ = tallies.Update(Tally.Keys.Of(now)).Add(Tally.Attrs.Count, 1).StoreKeyInputs().ToRequestJson();""")]
    [Arguments("""_ = shop.Update("a").Set("Status", "x").ToRequestJson();""")]
    [Arguments("""_ = hands.Update(new Hand.Key()).ToRequestJson();""")]
    [Arguments("""_ = orders.Delete(key).ToRequestJson();""")]
    [Arguments("""Mine.Update();""")]
    [Arguments("""_ = client.TransactWrite().Update(orders, key).Set(Order.Attrs.Note, "x").IfExists().ToRequestJson();""")]
    [Arguments("""_ = client.TransactWrite().Update(orders, key).Set(Order.Attrs.Note, "x").Touched().ToRequestJson();""")]
    [Arguments("""UpdateStep step = client.TransactWrite().Update(orders, key).Set(Order.Attrs.Note, "x"); _ = step.IfExists().ToRequestJson();""")]
    public async Task Analyze_UpdateThatCannotCreateAnUnreadableItemOrCannotBeFollowed_ReportsNothing(string statement)
    {
        // Arrange (an update of an item that exists, one that writes every member, an entity without such members, and a command or a step that goes where a clause can still follow)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("private static UpdateCommand<Order> Start() => orders.Update(key);")]
    [Arguments("private static string Run<T, TKey>(Table<T> table, TKey key) where T : IDynamoDbEntity<T> where TKey : IKey<T> => table.Update(key).ToRequestJson();")]
    public async Task Analyze_UpdateThatLeavesTheMemberOrOfATypeParameter_ReportsNothing(string member)
    {
        // Arrange (the caller of a helper may add IfExists, and a type parameter has no model)
        var source = AnalyzerRun.Member(Fields + member);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Message(string missing, string entity) =>
        $"DYNA241 Warning: If no item has this key, the Update creates one without {missing}, and every read of '{entity}' then throws DynamoDbMappingException. Call IfExists() to update only an item that exists, or set {missing} as well.";

    private static string Run(string statement) => AnalyzerRun.Member(Fields + "public static void Run()\n{\n" + statement + "\n}\n", Outside);

    private static Task<string> ReportAsync(string source) => Shared.ReportWithAsync(source, new UnreadableUpdateAnalyzer());
}
