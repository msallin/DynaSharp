using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA240: a <c>CreateTable()</c> chain that leaves out an entity that declares the same table.</summary>
public sealed class LeftOutEntityAnalyzerTests
{
    internal const string Model = """
        [DynamoDbEntity(Table = "store"), Discriminator("ORDER"), PartitionKey("ORDER#{Id}")]
        public partial class Order { public string Id { get; set; } = "id"; }

        [DynamoDbEntity(Table = "store"), Discriminator("CUSTOMER"), PartitionKey("CUSTOMER#{Id}")]
        public partial class Customer { public string Id { get; set; } = "id"; }

        [DynamoDbEntity(Table = "store"), Discriminator("PRODUCT"), PartitionKey("PRODUCT#{Id}"), GlobalIndexKey("ByName", PartitionKey = "NAME#{Name}")]
        public partial class Product { public string Id { get; set; } = "id"; public string Name { get; set; } = "n"; }

        [DynamoDbEntity(Table = "sessions"), Discriminator("SESSION"), PartitionKey("SESSION#{Id}")]
        public partial class Session { public string Id { get; set; } = "id"; }

        [DynamoDbEntity, Discriminator("LOOSE"), PartitionKey("LOOSE#{Id}")]
        public partial class Loose { public string Id { get; set; } = "id"; }

        [DynamoDbEntity(Table = "store")]
        public partial class Broken { public string Id { get; set; } = "id"; }

        public static class Hidden
        {
            [DynamoDbEntity(Table = "store"), Discriminator("SECRET"), PartitionKey("SECRET#{Id}")]
            private partial class Secret { public string Id { get; set; } = "id"; }
        }

        public static class Own
        {
            public sealed class Table<T> { public CreateTableCommand CreateTable() => null!; public CreateTableCommand CreateTable(int read) => null!; }

            public sealed class Table { public CreateTableCommand CreateTable() => null!; }
        }

        public static class Helpers
        {
            public static CreateTableCommand WithEverything(this CreateTableCommand command) => command;

            public static Task Twice(this int times, CreateTableCommand command) => Task.CompletedTask;

            public static Task Send(CreateTableCommand command) => Task.CompletedTask;

            public static CreateTableCommand Keep(CreateTableCommand command) => command;

            public static Table<Order> Open(DynamoDbClient client) => client.Table<Order>();
        }

        """;

    private const string Run = """
        public static async Task Run(DynamoDbClient client, Table<Order> orders, DynaSharp.Testing.FakeDynamoDb fake, CancellationToken ct)
        {

        """;

    [Test]
    [Arguments("await client.Table<Order>().[|CreateTable|]().Including<Customer>().SendAsync(ct);", "'Product'")]
    [Arguments("await orders.[|CreateTable|]().IfNotExists().WaitUntilActive().SendAsync(ct);", "'Customer', 'Product'")]
    [Arguments("await orders.[|CreateTable|]().Including<Product>();", "'Customer'")]
    [Arguments("await orders.[|CreateTable|]();", "'Customer', 'Product'")]
    [Arguments("_ = orders.[|CreateTable|]().Including<Customer>().ToRequest();", "'Product'")]
    [Arguments("_ = orders.[|CreateTable|]().Including<Customer>().ToRequestJson();", "'Product'")]
    [Arguments("_ = client.Table<Order>()?.[|CreateTable|]().Including<Customer>().ToRequestJson();", "'Product'")]
    [Arguments("await Helpers.Open(client).[|CreateTable|]().Including<Customer>().SendAsync(ct);", "'Product'")]
    public async Task Analyze_ChainThatLeavesOutAnEntityOfTheTable_ReportsCreateTable(string statement, string missing)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Order", "store", missing)));
    }

    [Test]
    [Arguments("await orders.CreateTable().Including<Customer>().Including<Product>().SendAsync(ct);")]
    [Arguments("await orders.CreateTable().Including<Product>().Including<Customer>().Including<Customer>().IfNotExists().SendAsync(ct);")]
    [Arguments("await GeneratorInput.DynamoDbEntities.IncludingAll(orders.CreateTable()).SendAsync(ct);")]
    [Arguments("await client.Table<Order>(\"store-test\").CreateTable().SendAsync(ct);")]
    [Arguments("await client.Table<Session>().CreateTable().SendAsync(ct);")]
    [Arguments("await client.Table<Loose>(\"loose\").CreateTable().SendAsync(ct);")]
    [Arguments("await client.Table(\"store\", \"pk\").CreateTable().SendAsync(ct);")]
    [Arguments("var command = orders.CreateTable(); await command.SendAsync(ct);")]
    [Arguments("await Helpers.Send(orders.CreateTable());")]
    [Arguments("await 1.Twice(orders.CreateTable());")]
    [Arguments("await orders.CreateTable().WithEverything().SendAsync(ct);")]
    [Arguments("await new Own.Table<Order>().CreateTable().SendAsync(ct);")]
    [Arguments("await new Own.Table<Order>().CreateTable(1).SendAsync(ct);")]
    [Arguments("await new Own.Table().CreateTable().SendAsync(ct);")]
    [Arguments("fake.CreateTable<Order>();")]
    [Arguments("_ = orders.CreateTable().GetHashCode();")]
    public async Task Analyze_ChainThatIsCompleteOrOutOfSight_ReportsNothing(string statement)
    {
        // Arrange
        var source = Source(statement);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("public static ValueTask<CreateTableResult> Make<T>(Table<Order> orders) where T : IDynamoDbEntity<T> => orders.CreateTable().Including<T>().SendAsync();")]
    [Arguments("public static ValueTask<CreateTableResult> Make<T>(Table<T> table) where T : IDynamoDbEntity<T> => table.CreateTable().SendAsync();")]
    public async Task Analyze_TypeParameter_ReportsNothing(string member)
    {
        // Arrange
        var source = AnalyzerRun.Member(member, Model);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("public static CreateTableCommand Command(DynamoDbClient client) => client.Table<Order>().[|CreateTable|]().Including<Customer>().IfNotExists().WaitUntilActive();", "'Product'")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) { return orders.[|CreateTable|]().Including<Product>(); }", "'Customer'")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) { CreateTableCommand Local() => orders.[|CreateTable|](); return Local(); }", "'Customer', 'Product'")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) { CreateTableCommand Local() { return orders.[|CreateTable|]().Including<Customer>(); } return Local(); }", "'Product'")]
    [Arguments("public static CreateTableCommand Command => new DynamoDbClient().Table<Order>().[|CreateTable|]().Including<Customer>();", "'Product'")]
    [Arguments("public static CreateTableCommand Command { get { return new DynamoDbClient().Table<Order>().[|CreateTable|]().Including<Customer>(); } }", "'Product'")]
    [Arguments("public static Func<Table<Order>, CreateTableCommand> Command = orders => orders.[|CreateTable|]().Including<Customer>();", "'Product'")]
    [Arguments("public static Func<Table<Order>, CreateTableCommand> Command = orders => { return orders.[|CreateTable|]().Including<Product>(); };", "'Customer'")]
    [Arguments("public static IEnumerable<CreateTableCommand> Commands(Table<Order> orders) { yield return orders.[|CreateTable|]().Including<Product>(); }", "'Customer'")]
    [Arguments("public static async Task<CreateTableCommand> Command(Table<Order> orders) { await Task.Yield(); return orders.[|CreateTable|]().Including<Product>(); }", "'Customer'")]
    public async Task Analyze_ReturnedChainThatLeavesOutAnEntityOfTheTable_ReportsCreateTable(string member, string missing)
    {
        // Arrange
        var source = AnalyzerRun.Member(member, Model);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Order", "store", missing)));
    }

    [Test]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) => orders.CreateTable().Including<Customer>().Including<Product>().IfNotExists();")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) => orders.CreateTable().WithEverything();")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) => GeneratorInput.DynamoDbEntities.IncludingAll(orders.CreateTable());")]
    [Arguments("public static object Command(Table<Order> orders) => orders.CreateTable();")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders, bool exists) => exists ? orders.CreateTable().IfNotExists() : orders.CreateTable();")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) { var command = orders.CreateTable(); return command; }")]
    [Arguments("public static CreateTableCommand Stored = new DynamoDbClient().Table<Order>().CreateTable();")]
    [Arguments("public static CreateTableCommand Command(Table<Order> orders) => Helpers.Keep(orders.CreateTable());")]
    [Arguments("public static void Command(Table<Order> orders) => orders.CreateTable();")]
    [Arguments("public static Action<Table<Order>> Command = orders => orders.CreateTable();")]
    public async Task Analyze_ReturnedChainThatIsCompleteOrOutOfSight_ReportsNothing(string member)
    {
        // Arrange
        var source = AnalyzerRun.Member(member, Model);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_EntityOfAReferencedAssemblyWithTheTableOfThatAssembly_NamesItAsTheFileCan()
    {
        // Arrange (Booking of DynaSharp.DefaultsFixture has the table "bookings" from [assembly: DynamoDbDefaults])
        var source = AnalyzerRun.Member(
            "public static Task Run(DynamoDbClient client) => client.Table<Visit>().[|CreateTable|]().SendAsync().AsTask();",
            """[DynamoDbEntity(Table = "bookings"), Discriminator(OptOut = true), PartitionKey("BOOKING#{Id}"), SortKey("AT#{At}")] public partial class Visit { public string Id { get; set; } = "id"; public string At { get; set; } = "at"; }""");

        // Act
        var diagnostic = (await DiagnosticsAsync(source)).Single();

        // Assert
        await Assert.That((diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), diagnostic.Properties[LeftOutEntityAnalyzer.FixProperty]))
            .IsEqualTo((Text("Visit", "bookings", "'DynaSharp.DefaultsFixture.Booking'"), "DynaSharp.DefaultsFixture.Booking"));
    }

    [Test]
    public async Task Analyze_EntitiesOfAReferencedAssembly_ReportsThePublicOnesAndLeavesOutTheInternal()
    {
        // Arrange
        var library = Library("""
            using DynaSharp.Mapping;
            namespace Features;
            [DynamoDbEntity(Table = "store"), Discriminator("LABEL"), PartitionKey("LABEL#{Id}")]
            public partial class Label { public string Id { get; set; } = "id"; }
            [DynamoDbEntity(Table = "store"), Discriminator("NOTE"), PartitionKey("NOTE#{Id}")]
            internal partial class Note { public string Id { get; set; } = "id"; }
            """);
        var source = Source("await orders.[|CreateTable|]().Including<Customer>().Including<Product>().SendAsync(ct);");

        // Act
        var report = AnalyzerRun.Describe([.. await DiagnosticsAsync(source, library)]);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Order", "store", "'Features.Label'")));
    }

    [Test]
    public async Task Analyze_EntityDerivedFromTheEntityOfTheTable_ReportsIt()
    {
        // Arrange (each entity has metadata of its own, such as an index the base lacks)
        var source = AnalyzerRun.Member(
            "public static Task Run(Table<Animal> animals) => animals.[|CreateTable|]().SendAsync().AsTask();",
            """
            [DynamoDbEntity(Table = "zoo"), Discriminator("ANIMAL")] public partial class Animal { [PartitionKey] public string Id { get; set; } = "id"; }
            [DynamoDbEntity(Table = "zoo"), Discriminator("BIRD"), GlobalIndexKey("ByWing", PartitionKey = "WING#{Wing}")] public partial class Bird : Animal { public string Wing { get; set; } = "w"; }
            """);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Animal", "zoo", "'Bird'")));
    }

    [Test]
    public async Task Analyze_ChainOverSeveralLines_ReportsOnce()
    {
        // Arrange
        var source = Source("""
            await client.Table<Customer>().[|CreateTable|]()
                .Including<Order>()
                .IfNotExists()
                .SendAsync(ct);
            """);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Customer", "store", "'Product'")));
    }

    internal static string Message(string owner, string table, string missing) => "DYNA240 Warning: " + Text(owner, table, missing);

    internal static string Source(string statement) => AnalyzerRun.Member(Run + statement + "\n}", Model);

    /// <summary>A library with its generated code, as a project or a package it references gives it.</summary>
    internal static MetadataReference Library(string source)
    {
        var compilation = AnalyzerRun.Compile(source).WithAssemblyName("Features");
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        return emitted.Success ? MetadataReference.CreateFromImage(image.ToArray()) : throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
    }

    private static string Text(string owner, string table, string missing) =>
        $"CreateTable() of '{owner}' leaves out entities that declare the table '{table}' as well: {missing}. The table is created without what they declare, such as their indexes. Add Including<T>() for each.";

    private static async Task<ImmutableArray<Diagnostic>> DiagnosticsAsync(string source, params MetadataReference[] references)
    {
        ImmutableArray<DiagnosticAnalyzer> analyzers = [new LeftOutEntityAnalyzer()];
        return await AnalyzerRun.Compile(source).AddReferences(references).WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }
}
