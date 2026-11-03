using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA230 to DYNA233: a type argument that does not meet its constraint, where the compiler has only CS0311, CS0315 or CS0453 to say.</summary>
public sealed class TypeArgumentAnalyzerTests
{
    private const string Model = """
        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("ORDER#{Id}")]
        public partial class Order
        {
            public required string Id { get; init; }

            public required string Name { get; init; }

            public int Count { get; init; }

            public int? Slot { get; init; }

            public List<string> Lines { get; init; } = [];
        }

        [DynamoDbEntity]
        [Discriminator("CUSTOMER")]
        [PartitionKey("CUSTOMER#{Id}")]
        public partial class Customer
        {
            public required string Id { get; init; }
        }

        [DynamoDbDocument]
        public partial class Address
        {
            public required string City { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator(OptOut = true)]
        [PartitionKey("LEGACY#{Id}")]
        public partial class LegacyRecord
        {
            public required string Id { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("BROKEN")]
        public class Broken
        {
            public required string Id { get; init; }
        }

        public class Plain
        {
        }

        """;

    private const string Run = "public static void Run(DynamoDbClient client, Table<Order> orders)\n{\n";

    private static readonly AnalyzerFixture Fixture = new(Model);

    [Test]
    [Arguments("""orders.Get([|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.Update([|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.Delete([|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.Get(key: [|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.Query([|Customer.Keys.Pk.Of("a")|]);""", "'PartitionKey<Customer>' is a partition key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.Get([|Order.Keys.Pk.Of("a")|]);""", "'PartitionKey<Order>' is the partition key of 'Order' alone, and this call needs the whole key of an item. Use Order.Keys.Of(...), or Query for every item of the partition.")]
    [Arguments("""shop.Get([|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table has no entity, so it takes the values of a key. Open it with Of<Customer>() to use the key.")]
    [Arguments("""shop.Get([|Customer.Keys.Pk.Of("a")|]);""", "'PartitionKey<Customer>' is the partition key of 'Customer' alone, and this call needs the whole key of an item. Open the table with Of<Customer>() and use Customer.Keys.Of(...).")]
    [Arguments("""orders.Get([|Guid.Empty|]);""", "'Guid' is not a key, and this table holds 'Order'. Build one with Order.Keys.Of(...).")]
    [Arguments("""orders.Get([|new int[0]|]);""", "'int[]' is not a key, and this table holds 'Order'. Build one with Order.Keys.Of(...).")]
    [Arguments("""client.TransactWrite().Update(orders, [|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""client.TransactWrite().Put(orders, new Order { Id = "a", Name = "b" }).Check(orders, [|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""TransactWriteExtensions.Delete(client.TransactWrite(), orders, [|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""client.TransactGet().Get(orders, [|Customer.Keys.Of("a")|], out _);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.BatchGet().Key([|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.BatchWrite().Delete([|Customer.Keys.Of("a")|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.BatchGet().Keys([|new Customer.Keys.PrimaryKey[1]|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    [Arguments("""orders.BatchWrite().DeleteAll([|new List<Customer.Keys.PrimaryKey>()|]);""", "'Customer.Keys.PrimaryKey' is a key of 'Customer', and this table holds 'Order'. Use a key of Order.Keys.")]
    public async Task Analyze_KeyThatDoesNotFitTheTable_ReportsTheKey(string statement, string message)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA230 Error: " + message));
    }

    [Test]
    [Arguments("""client.Table<[|Plain|]>("x");""", "'Plain' is no entity: without [DynamoDbEntity] nothing generates its mapping. Add [DynamoDbEntity] and a [PartitionKey], and make the type partial.")]
    [Arguments("""shop.Of<[|Plain|]>();""", "'Plain' is no entity: without [DynamoDbEntity] nothing generates its mapping. Add [DynamoDbEntity] and a [PartitionKey], and make the type partial.")]
    [Arguments("""shop.Index<[|Plain|]>("x");""", "'Plain' is no entity: without [DynamoDbEntity] nothing generates its mapping. Add [DynamoDbEntity] and a [PartitionKey], and make the type partial.")]
    [Arguments("""shop.Scan().Of<[|Plain|], Order>();""", "'Plain' is no entity: without [DynamoDbEntity] nothing generates its mapping. Add [DynamoDbEntity] and a [PartitionKey], and make the type partial.")]
    [Arguments("""orders.CreateTable().Including<[|Address|]>();""", "'Address' is a document, which is stored inside an entity and has no key of its own. A table holds entities: use [DynamoDbEntity] with a [PartitionKey].")]
    [Arguments("""client.Table<[|Address|]>("x");""", "'Address' is a document, which is stored inside an entity and has no key of its own. A table holds entities: use [DynamoDbEntity] with a [PartitionKey].")]
    [Arguments("""shop.Query("a").Of<Order, [|LegacyRecord|]>();""", "'LegacyRecord' has no discriminator, and a read of several types tells them apart by it. Give it [Discriminator(\"LEGACY_RECORD\")] in place of the opt-out.")]
    [Arguments("""_ = new Item().Is<[|LegacyRecord|]>();""", "'LegacyRecord' has no discriminator, and a read of several types tells them apart by it. Give it [Discriminator(\"LEGACY_RECORD\")] in place of the opt-out.")]
    public async Task Analyze_TypeArgumentThatIsNoEntity_ReportsTheTypeArgument(string statement, string message)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA231 Error: " + message));
    }

    [Test]
    public async Task Analyze_TableOfATypeThatIsNoEntity_ReportsTheTypeOnce()
    {
        // Arrange: the step of the transaction infers the same type from the table, and that is not reported again.
        var source = AnalyzerRun.Member("public static void Run(DynamoDbClient client, Table<[|Plain|]> plains) => client.TransactWrite().Put(plains, new Plain());");

        // Act
        var report = await Fixture.ReportWithAsync(source, new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA231 Error: 'Plain' is no entity: without [DynamoDbEntity] nothing generates its mapping. Add [DynamoDbEntity] and a [PartitionKey], and make the type partial."));
    }

    [Test]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Name, [|5|]);""", "'Order.Attrs.Name' holds 'string', and this value is 'int'. Pass a value of 'string'")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).SetIfNotNull(Order.Attrs.Name, [|5|]);""", "'Order.Attrs.Name' holds 'string', and this value is 'int'. Pass a value of 'string'")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Name, [|(int?)5|]);""", "'Order.Attrs.Name' holds 'string', and this value is 'int?'. Pass a value of 'string'")]
    [Arguments("""client.TransactWrite().Update(orders, Order.Keys.Of("a")).Set(Order.Attrs.Name, [|5|]);""", "'Order.Attrs.Name' holds 'string', and this value is 'int'. Pass a value of 'string'")]
    public async Task Analyze_ValueOfAnotherTypeThanItsHandle_ReportsTheValue(string statement, string message)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA232 Error: The handle " + message + ", or the handle of the attribute you meant."));
    }

    [Test]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Add([|Order.Attrs.Name|], "x");""", "'Order.Attrs.Name' holds 'string'")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Add([|Order.Attrs.Lines|], new List<string>());""", "'Order.Attrs.Lines' holds 'List<string>'")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Add([|Order.Attrs.Name|], 5);""", "'Order.Attrs.Name' holds 'string'")]
    [Arguments("""client.TransactWrite().Update(orders, Order.Keys.Of("a")).Add([|Order.Attrs.Name|], "x");""", "'Order.Attrs.Name' holds 'string'")]
    public async Task Analyze_AddOnAHandleThatIsNoNumberOrSet_ReportsTheHandle(string statement, string holds)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA233 Error: Add adds to a number or a set, and " + holds + ". Use Set to write the value."));
    }

    [Test]
    [Arguments("""orders.Get(Order.Keys.Of("a"));""")]
    [Arguments("""orders.Query(Order.Keys.Pk.Of("a"));""")]
    [Arguments("""shop.Query(Customer.Keys.Pk.Of("a"));""")]
    [Arguments("""shop.Get(Guid.Empty);""")]
    [Arguments("""orders.Get(Customer.Keys.Of("a"), "b", "c");""")]
    [Arguments("""orders.Get(nope: Customer.Keys.Of("a"));""")]
    [Arguments("""orders.Get(new Nope());""")]
    [Arguments("""client.TransactWrite().Update("x", Customer.Keys.Of("a"));""")]
    [Arguments("""client.TransactWrite().Update(partitionKey: "a", table: null);""")]
    [Arguments("""client.Table<Order>("x");""")]
    [Arguments("""client.Table<Broken>("x");""")]
    [Arguments("""client.Table<AttributeValue>("x");""")]
    [Arguments("""shop.Index<Item>("x");""")]
    [Arguments("""orders.Get<int, int>(1);""")]
    [Arguments("""_ = new List<Plain>();""")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Name, "x").Set(Order.Attrs.Count, 1).Add(Order.Attrs.Count, 1);""")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Count, "x");""")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Slot, "x");""")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Name, () => 1);""")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(null, "x");""")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set("x", 1, 2);""")]
    [Arguments("""orders.Undefined(Customer.Keys.Of("a"));""")]
    [Arguments("""Console.WriteLine(Customer.Keys.Of("a"), 1, 2);""")]
    public async Task Analyze_WhatTheConstraintsTakeOrTheCompilerExplains_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("public static void Run<T>(Table<T> table) where T : IDynamoDbEntity<T> => table.Get(Customer.Keys.Of(\"a\"));")]
    [Arguments("public static void Run<TKey>(Table<Order> orders, TKey key) => orders.Get(key);")]
    public async Task Analyze_GenericHelper_ReportsNothing(string member)
    {
        // Arrange: a type parameter of the consumer's own says nothing about what the call needs.
        var source = AnalyzerRun.Member(member);

        // Act
        var report = await Fixture.ReportWithAsync(source, new TypeArgumentAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CompilationWithoutTheLibrary_ReportsNothing()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation("public static class Code { public static void Run() => System.Console.WriteLine(new System.Collections.Generic.List<int>()); }", Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest)
            .RemoveAllReferences().AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        // Act
        var diagnostics = await compilation.WithAnalyzers([new TypeArgumentAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("""orders.Get(Customer.Keys.Of("a"));""", "CS0315")]
    [Arguments("""client.Table<Plain>("x");""", "CS0311")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Name, 5);""", "CS0453")]
    [Arguments("""orders.Update(Order.Keys.Of("a")).Add(Order.Attrs.Name, "x");""", "CS0315")]
    public async Task Compile_TheSameMistake_FailsWithTheErrorTheRuleStandsBeside(string statement, string expected)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var errors = Fixture.CompilerErrors(source);

        // Assert
        await Assert.That(errors.Select(static error => error.Id)).Contains(expected);
    }
}
