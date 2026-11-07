using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA238: a member that opens the table an entity declares, called for an entity that declares none.</summary>
public sealed class DeclaredTableAnalyzerTests
{
    private const string Model = """
        [DynamoDbEntity, Discriminator("ORDER"), PartitionKey("ORDER#{Id}")]
        public partial class Order { public string Id { get; set; } = "id"; }

        [DynamoDbEntity(Table = "carts"), Discriminator("CART"), PartitionKey("CART#{Id}")]
        public partial class Cart { public string Id { get; set; } = "id"; }

        public sealed class Hand : IDynamoDbEntity<Hand> { }

        """;

    private const string Run = """
        public static void Run(DynamoDbClient client, DynaSharp.Testing.FakeDynamoDb fake, Order order, Item key)
        {

        """;

    [Test]
    [Arguments("_ = client.[|Table<Order>|]();", "Table")]
    [Arguments("_ = client?[|.Table<Order>()|];", "Table")]
    [Arguments("fake.[|CreateTable<Order>|]();", "CreateTable")]
    [Arguments("fake.[|Put|](order);", "Put")]
    [Arguments("_ = fake.[|Get<Order>|](key);", "Get")]
    [Arguments("_ = fake.[|Items<Order>|]();", "Items")]
    [Arguments("_ = fake.[|Delete|](order);", "Delete")]
    [Arguments("_ = fake.[|DescribeTable<Order>|]();", "DescribeTable")]
    [Arguments("_ = fake.[|EnableStream<Order>|](DynaSharp.Requests.StreamViewType.NewImage);", "EnableStream")]
    [Arguments("_ = fake.[|DisableStream<Order>|]();", "DisableStream")]
    [Arguments("_ = fake.[|TableItems<Order>|]();", "TableItems")]
    [Arguments("_ = fake.[|GetItem|](Order.Keys.Of(\"o-1\"));", "GetItem")]
    [Arguments("fake.[|PutItem<Order>|](key);", "PutItem")]
    [Arguments("_ = DynaSharp.Testing.StubResponse.[|Table<Order>|]();", "Table")]
    public async Task Analyze_EntityWithoutATable_ReportsTheCall(string statement, string method)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new DeclaredTableAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(
            source,
            $"DYNA238 Error: 'Order' declares no table, so {method} without the name of a table throws InvalidOperationException. Declare one with [DynamoDbEntity(Table = \"...\")] on the type or [assembly: DynamoDbDefaults(Table = \"...\")], or pass the name of the table."));
    }

    [Test]
    [Arguments("_ = client.Table<Cart>();")]
    [Arguments("_ = client.Table<Order>(\"orders\");")]
    [Arguments("fake.CreateTable<Order>(\"orders\");")]
    [Arguments("fake.Put(\"orders\", order);")]
    [Arguments("_ = DynaSharp.Testing.StubResponse.Table<Order>(\"orders\");")]
    [Arguments("_ = client.Table<Hand>();")]
    [Arguments("_ = client.Table(\"orders\", \"pk\");")]
    [Arguments("_ = client.TransactWrite();")]
    [Arguments("_ = Item.From(order);")]
    public async Task Analyze_DeclaredTableOrExplicitName_ReportsNothing(string statement)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new DeclaredTableAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_TypeParameter_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member("public static Table<T> Open<T>(DynamoDbClient client) where T : IDynamoDbEntity<T> => client.Table<T>();", Model);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new DeclaredTableAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_TableOfTheAssembly_ReportsNothing()
    {
        // Arrange
        var source = Source("_ = client.Table<Order>();").Replace("namespace Shop;", "[assembly: DynamoDbDefaults(Table = \"appdata\")]\n\nnamespace Shop;", StringComparison.Ordinal);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new DeclaredTableAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_EntityOfAnotherAssemblyWithTheTableOfThatAssembly_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member("public static void Run(DynamoDbClient client) { _ = client.Table<Booking>(); }")
            .Replace("namespace Shop;", "using DynaSharp.DefaultsFixture;\n\nnamespace Shop;", StringComparison.Ordinal);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new DeclaredTableAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Source(string statement) => AnalyzerRun.Member(Run + statement + "\n}", Model);
}
