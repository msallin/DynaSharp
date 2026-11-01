using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA203: a statement whose value is a command, which nothing sends, and which leaves the handle it was called on used up.</summary>
public sealed class DiscardedCommandAnalyzerTests
{
    private const string Message = "DYNA203 Error: This DynaSharp command is discarded, so it is never sent, and the handle it came from is used up. Send it, or use what the call returns.";

    private const string Order = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        public partial class Order
        {
            public required string UserId { get; init; }
        }

        public static class Audit
        {
            public static UpdateCommand<T> Stamped<T>(this UpdateCommand<T> command) where T : IDynamoDbDocument<T> => command.Set("at", 1);

            public static TCommand Owned<TCommand>(this TCommand command) where TCommand : IConditional<TCommand>, allows ref struct => command.If(Attr("owner").Exists());
        }

        public delegate void Build(QueryCommand<Item> query);

        public delegate QueryCommand<Item> Extend(QueryCommand<Item> query);
        """;

    private const string Builders = """

        public sealed class Builder { public Builder Add() => this; }

        public ref struct Ref { public Ref Add() => this; }

        public interface IOwnFluent<TSelf> where TSelf : allows ref struct { }
        """ + "\nnamespace DynaSharp.Extras { public ref struct Own { public static Own Make() => default; } }\nnamespace Acme.DynaSharp { public ref struct Foreign { public static Foreign Make() => default; } }\n";

    private const string Fields = """
        private static readonly Table<Order> orders = null!;
        private static readonly DynamoDbClient client = null!;
        private static readonly Order order = null!;

        """;

    [Test]
    [Arguments("""[|orders.Put(order)|];""")]
    [Arguments("""[|shop.Put()|];""")]
    [Arguments("""[|shop.Get("a")|];""")]
    [Arguments("""[|shop.Update("a")|];""")]
    [Arguments("""[|shop.Delete("a")|];""")]
    [Arguments("""[|shop.Query("a")|];""")]
    [Arguments("""[|orders.Query(default(Order.Keys.PrimaryKey))|];""")]
    [Arguments("""[|orders.Query(default(Order.Keys.PrimaryKey)).Limit(1)|];""")]
    [Arguments("""[|shop.Scan()|];""")]
    [Arguments("""[|shop.Scan().Parallel(8)|];""")]
    [Arguments("""[|shop.BatchGet()|];""")]
    [Arguments("""[|shop.BatchWrite()|];""")]
    [Arguments("""[|shop.Query(["a", "b"])|];""")]
    [Arguments("""[|shop.Query("a").Of<Order, Order>()|];""")]
    [Arguments("""[|shop.Update("a").Return(new Attr("n"))|];""")]
    [Arguments("""[|shop.Update("a").Set("n", 1).If(Attr("n").Exists())|];""")]
    [Arguments("""[|client.TransactGet()|];""")]
    [Arguments("""[|client.TransactWrite()|];""")]
    [Arguments("""[|client.TransactWrite().Put(orders, order)|];""")]
    [Arguments("""[|client.TransactWrite().Update(shop, "a")|];""")]
    [Arguments("""[|client.TransactWrite().Update(shop, "a").Set("n", 1)|];""")]
    [Arguments("""[|client.TransactWrite().Delete(shop, "a")|];""")]
    [Arguments("""[|client.TransactWrite().Put(orders, order).IfNotExists()|];""")]
    [Arguments("""[|shop.Update("a").Stamped()|];""")]
    [Arguments("""[|shop.Update("a").Owned()|];""")]
    public async Task Analyze_CommandAsAStatement_ReportsTheExpression(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run()\n{\n" + statement + "\n}\n", Order);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_ResultOfAFluentCallIgnored_ReportsTheCall()
    {
        // Arrange (the shape that took a handle from a working chain: the next use throws at run time)
        var source = AnalyzerRun.Statements("""
            var query = shop.Query("a");
            [|query.Descending()|];
            _ = query.ToRequestJson();
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_InsideAVoidHelper_ReportsTheCall()
    {
        // Arrange
        var source = AnalyzerRun.Member("""private static void ApplyFilter(QueryCommand<Item> query) { [|query.Filter(Attr("n").Exists())|]; }""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_GenericHelperOverACommand_ReportsTheCall()
    {
        // Arrange
        var source = AnalyzerRun.Member("""private static void Apply<TCommand>(TCommand command) where TCommand : IConditional<TCommand>, allows ref struct { [|command.If(Attr("n").Exists())|]; }""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_BodyOfAVoidMember_ReportsTheExpression()
    {
        // Arrange (an expression body of a void method drops the value as a statement would)
        var source = AnalyzerRun.Member("""private static void Run() => [|shop.Update("a")|];""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_BodyOfAVoidLambda_ReportsTheExpression()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""Action act = () => [|shop.Update("a")|];""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_AfterTheFirstStatementOfALoopHeader_ReportsTheIncrement()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""for (var i = 0; i < 1; [|shop.Update("a")|], i++) { }""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""var command = shop.Update("a");""")]
    [Arguments("""_ = shop.Update("a");""")]
    [Arguments("""var command = shop.Update("a"); command = command.Set("n", 1); _ = command.ToRequestJson();""")]
    [Arguments("""_ = shop.Update("a").ToRequestJson();""")]
    [Arguments("""shop.Update("a").ToRequestJson();""")]
    [Arguments("""shop.Update("a").SendAsync();""")]
    [Arguments("""_ = shop.Update("a").SendAsync();""")]
    [Arguments("""shop.Query("a").ItemAsync();""")]
    [Arguments("""_ = orders.Query(default(Order.Keys.PrimaryKey)).ItemAsync();""")]
    [Arguments("""Extend extend = static query => query.Limit(1);""")]
    [Arguments("""System.Threading.Tasks.Task.Run(() => shop.Update("a").SendAsync().AsTask());""")]
    public async Task Analyze_ValueThatIsUsedOrSent_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static void Run()\n{\n" + statement + "\n}\n", Order);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""await shop.Update("a");""")]
    [Arguments("""await shop.Update("a").Set("n", 1);""")]
    [Arguments("""await shop.Update("a").Set("n", 1).SendAsync();""")]
    [Arguments("""await client.TransactWrite().Update(shop, "a").Set("n", 1);""")]
    [Arguments("""await shop.Query("a").CountAsync();""")]
    [Arguments("""await shop.Query("a").ItemAsync();""")]
    [Arguments("""await orders.Query(default(Order.Keys.PrimaryKey)).Filter(Attr("n").Exists()).ItemAsync();""")]
    public async Task Analyze_AwaitOfAChain_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Fields + "public static async Task Run()\n{\n" + statement + "\n}\n", Order);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""private static UpdateCommand<Item> Run() => shop.Update("a");""")]
    [Arguments("""private static UpdateCommand<Item> Run() { return shop.Update("a"); }""")]
    [Arguments("""private static UpdateCommand<Item> Run(UpdateCommand<Item> command) => command.Set("n", 1);""")]
    [Arguments("""private static TCommand Run<TCommand>(TCommand command) where TCommand : IConditional<TCommand>, allows ref struct => command.If(Attr("n").Exists());""")]
    public async Task Analyze_ReturnedCommand_ReportsNothing(string member)
    {
        // Arrange
        var source = AnalyzerRun.Member(member);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""new Builder().Add();""")]
    [Arguments("""new Ref().Add();""")]
    [Arguments("""DynaSharp.Extras.Own.Make();""")]
    [Arguments("""Acme.DynaSharp.Foreign.Make();""")]
    [Arguments("""Keep(1);""")]
    public async Task Analyze_FluentCallThatIsNoCommand_ReportsNothing(string statement)
    {
        // Arrange (a builder of the user's own is free to work by changing itself, and Keep is a generic method whose type argument is no ref struct)
        var source = AnalyzerRun.Member("private static T Keep<T>(T value) => value;\npublic static void Run()\n{\n" + statement + "\n}\n", Builders);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""<T>(T value) where T : IDisposable, allows ref struct""", "T")]
    [Arguments("""<T>(T value) where T : IEquatable<int>, allows ref struct""", "T")]
    [Arguments("""<T>(T value) where T : IOwnFluent<T>, allows ref struct""", "T")]
    [Arguments("""<T>(T value) where T : IConditional<T>""", "T")]
    [Arguments("""<T, U>(T value) where T : U, allows ref struct where U : allows ref struct""", "T, U")]
    public async Task Analyze_TypeParameterThatIsNoCommand_ReportsNothing(string signature, string arguments)
    {
        // Arrange (only a type that can be a ref struct and has a fluent interface of the library over itself stands for a command)
        var source = AnalyzerRun.Member($$"""private static T Keep{{signature}} => value; private static void Run{{signature}} { Keep<{{arguments}}>(value); }""", Builders);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ClassCommand_ReportsNothing()
    {
        // Arrange (creating a table is a class with state of its own: a call on a variable of it is no lost handle)
        var source = AnalyzerRun.Statements("""var create = shop.CreateTable(); create.IfNotExists();""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_WriterOfAConverter_ReportsNothing()
    {
        // Arrange (the ref structs below the root namespace are no commands)
        var source = AnalyzerRun.Member("""private static void Run(DynamoDbWriter writer) { writer.WriteString("a"); }""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CodeWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var source = "public static class Code { public static void Run() { System.Console.WriteLine(1); } }";

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static Task<string> ReportAsync(string source) => AnalyzerRun.ReportWithAsync(source, new DiscardedCommandAnalyzer());
}
