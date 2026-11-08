using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA203, applied in a real workspace to what <see cref="DiscardedCommandAnalyzer"/> reports there, and analyzed again.</summary>
public sealed class DiscardedCommandCodeFixTests
{
    private const string Send = "Send it with await and SendAsync()";

    private const string Discard = "Discard it with _ =";

    private const string Model = """

        [DynamoDbEntity(Table = "orders")]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        public partial class Order
        {
            public required string UserId { get; init; }
        }
        """;

    private const string Fields = """
        private static readonly Table<Order> orders = null!;
        private static readonly DynamoDbClient client = null!;
        private static readonly Order order = null!;
        private static readonly object gate = new();

        """;

    private static readonly CodeFixFixture Fixture = new(Model, new DiscardedCommandAnalyzer(), new DiscardedCommandCodeFix());

    [Test]
    [Arguments("public static async Task Run() { orders.Put(order); }", new[] { Send, Discard })]
    [Arguments("public static async Task Run() { client.TransactWrite().Put(orders, order); }", new[] { Send, Discard })]
    [Arguments("public static void Run() { orders.Put(order); }", new[] { Discard })]
    [Arguments("public static async Task Run() { lock (gate) { orders.Put(order); } }", new[] { Discard })]
    [Arguments("public static void Run() { lock (gate) { Func<Task> send = async () => { orders.Put(order); }; } }", new[] { Send, Discard })]
    [Arguments("public static void Run() { lock (gate) { async Task SendAsync() { orders.Put(order); } } }", new[] { Send, Discard })]
    [Arguments("""public static async Task Run() { shop.Query("a"); }""", new[] { Discard })]
    [Arguments("""public static async Task Run() { var query = shop.Query("a"); query.Limit(1); await foreach (var item in query.ItemsAsync()) { } }""", new[] { "Assign it back to 'query'", Discard })]
    [Arguments("""public static void Run() { var query = shop.Query("a"); query.Select(Attr<int>("n")); }""", new[] { Discard })]
    [Arguments("""private static void Apply(QueryCommand<Item> query) { query.Limit(1); }""", new[] { Discard })]
    [Arguments("""private static void Run() { Action<int> run = _ => shop.Update("a"); }""", new string[0])]
    public async Task Titles_Command_OfferWhatFitsWhereItStands(string member, string[] titles)
    {
        // Arrange (a send needs an async function and no lock around the statement, an assignment a local that takes what the call returns, a discard a _ that names nothing)
        var source = AnalyzerRun.Member(Fields + member);

        // Act
        var offered = await Fixture.TitlesAsync(source);

        // Assert
        await Assert.That(offered).IsEquivalentTo(titles);
    }

    [Test]
    [Arguments(DiscardedCommandCodeFix.SendKey, "public static async Task Run()\n{\n    orders.Put(order);\n}\n", "public static async Task Run()\n{\n    await orders.Put(order).SendAsync();\n}\n")]
    [Arguments(DiscardedCommandCodeFix.SendKey, "public static void Run() { Func<Task> send = async () => orders.Put(order); }", "public static void Run() { Func<Task> send = async () => await orders.Put(order).SendAsync(); }")]
    [Arguments(DiscardedCommandCodeFix.AssignKey, """public static void Run() { var query = shop.Query("a").Limit(2); query.Descending().Limit(1); _ = query.ToRequestJson(); }""", """public static void Run() { var query = shop.Query("a").Limit(2); query = query.Descending().Limit(1); _ = query.ToRequestJson(); }""")]
    [Arguments(DiscardedCommandCodeFix.DiscardKey, "public static void Run()\n{\n    orders.Put(order);\n}\n", "public static void Run()\n{\n    _ = orders.Put(order);\n}\n")]
    [Arguments(DiscardedCommandCodeFix.DiscardKey, """private static void Run() => shop.Update("a");""", """private static void Run() => _ = shop.Update("a");""")]
    public async Task Fix_Command_WritesItAndLeavesNoDiagnostic(string key, string member, string expected)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + member);

        // Act
        var fixedSource = await Fixture.FixAsync(source, key);

        // Assert
        await Assert.That((fixedSource, await Fixture.ReportAsync(fixedSource), (await Fixture.CompilerErrorsAsync(fixedSource)).Count)).IsEqualTo((AnalyzerRun.Member(Fields + expected), string.Empty, 0));
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    public async Task FixAll_Send_SendsEveryCommand(FixAllScope scope)
    {
        // Arrange (a read has no SendAsync, and stays as it was)
        var source = AnalyzerRun.Member(Fields + """public static async Task Run() { orders.Put(order); orders.Delete(order); shop.Query("a"); }""");

        // Act
        var fixedSource = await Fixture.FixAllAsync(source, DiscardedCommandCodeFix.SendKey, scope);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(AnalyzerRun.Member(Fields + """public static async Task Run() { await orders.Put(order).SendAsync(); await orders.Delete(order).SendAsync(); shop.Query("a"); }"""));
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new DiscardedCommandCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA203"]);
    }

    [Test]
    public async Task GetFixAllProvider_FixesAllInOneGo()
    {
        // Arrange
        var fix = new DiscardedCommandCodeFix();

        // Act
        var provider = fix.GetFixAllProvider();

        // Assert
        await Assert.That(provider).IsEqualTo(WellKnownFixAllProviders.BatchFixer);
    }
}
