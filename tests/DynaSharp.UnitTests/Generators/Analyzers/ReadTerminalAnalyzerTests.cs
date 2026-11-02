using System.Reflection;
using DynaSharp.Fluent;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA202: a query or scan that is awaited or sent, where the compiler only has a constraint to complain about.</summary>
public sealed class ReadTerminalAnalyzerTests
{
    private const string Message = "DYNA202 Warning: A read is not awaited; end the chain with ItemsAsync, ValuesAsync or another terminal it has";

    private const string Order = """
        namespace Shop
        {
            [DynamoDbEntity]
            [Discriminator("ORDER")]
            public partial class Order
            {
                [PartitionKey("ORDER#{Id}")]
                [DynamoDbAttribute("pk")]
                public required string Id { get; init; }
            }
        }

        """;

    [Test]
    [Arguments("""[|await shop.Query("a")|];""")]
    [Arguments("""[|await shop.Scan()|];""")]
    [Arguments("""[|await shop.Query("a").Limit(1)|];""")]
    [Arguments("""[|await shop.Scan().Parallel(8)|];""")]
    [Arguments("""[|await (shop.Query("a"))|];""")]
    [Arguments("""[|await shop.Query("a").Select(Attr<int>("n"))|];""")]
    [Arguments("""[|await shop.Scan().Select(Attr<int>("n"))|];""")]
    [Arguments("""[|await shop.Query(["a", "b"])|];""")]
    [Arguments("""[|await shop.Query(["a", "b"]).Limit(1)|];""")]
    [Arguments("""[|await shop.Query("a").Of<Order, Order>()|];""")]
    [Arguments("""[|await shop.Query("a").Of<Order, Order, Order>()|];""")]
    [Arguments("""[|await shop.Query("a").Of<Order, Order, Order, Order>()|];""")]
    [Arguments("""[|await shop.Scan().Of<Order, Order>()|];""")]
    [Arguments("""[|await shop.Scan().Of<Order, Order, Order>()|];""")]
    [Arguments("""[|await shop.Scan().Of<Order, Order, Order, Order>()|];""")]
    public async Task Analyze_AwaitOfAReadCommand_ReportsTheAwait(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member($$"""public static async Task Run() { {{statement}} }""", Order);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""shop.Query("a").[|SendAsync|]();""")]
    [Arguments("""shop.Scan().[|SendAsync|](CancellationToken.None);""")]
    [Arguments("""shop.Query("a").Filter(Attr("x").Exists()).[|SendAsync|]();""")]
    [Arguments("""shop.Query("a").Select(Attr<int>("n")).[|SendAsync|]();""")]
    [Arguments("""shop.Scan().Select(Attr<int>("n")).[|SendAsync|]();""")]
    [Arguments("""shop.Scan().Parallel(8).[|SendAsync|]();""")]
    [Arguments("""shop.Query(["a", "b"]).[|SendAsync|]();""")]
    [Arguments("""shop.Query("a").Of<Order, Order>().[|SendAsync|]();""")]
    [Arguments("""shop.Query("a").Of<Order, Order, Order>().[|SendAsync|]();""")]
    [Arguments("""shop.Query("a").Of<Order, Order, Order, Order>().[|SendAsync|]();""")]
    [Arguments("""shop.Scan().Of<Order, Order>().[|SendAsync|]();""")]
    [Arguments("""shop.Scan().Of<Order, Order, Order>().[|SendAsync|]();""")]
    [Arguments("""shop.Scan().Of<Order, Order, Order, Order>().[|SendAsync|]();""")]
    public async Task Analyze_SendOfAReadCommand_ReportsTheName(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Statements(statement, Order);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_AwaitOfTheSend_ReportsOnce()
    {
        // Arrange: the await is of a call that failed, which has no type to look at.
        var source = AnalyzerRun.Member("""public static async Task Run() => await shop.Query("a").[|SendAsync|]();""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""await foreach (var item in shop.Query("a").ItemsAsync()) { }""")]
    [Arguments("""await foreach (var page in shop.Query("a").PagesAsync()) { }""")]
    [Arguments("""await shop.Query("a").PageAsync();""")]
    [Arguments("""await shop.Query("a").CountAsync();""")]
    [Arguments("""await foreach (var item in shop.Scan().ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in shop.Scan().Parallel(8).ItemsAsync()) { }""")]
    [Arguments("""await shop.Scan().CountAsync();""")]
    [Arguments("""await foreach (var item in shop.Query(["a", "b"]).ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in shop.Query("a").Of<Order, Order>().ItemsAsync()) { }""")]
    [Arguments("""await foreach (var item in shop.Scan().Of<Order, Order>().ItemsAsync()) { }""")]
    [Arguments("""await foreach (var value in shop.Query("a").Select(Attr<int>("n")).ValuesAsync()) { }""")]
    [Arguments("""await shop.Query("a").Select(Attr<int>("n")).ValueAsync();""")]
    [Arguments("""await foreach (var value in shop.Scan().Select(Attr<int>("n")).ValuesAsync()) { }""")]
    [Arguments("""await shop.Get("a");""")]
    [Arguments("""await shop.Get("a").Select(Attr<int>("n"));""")]
    [Arguments("""await shop.Get("a").Select(Attr<int>("n")).SendAsync();""")]
    [Arguments("""await shop.Get("a").SendAsync();""")]
    [Arguments("""await shop.Update("a").Set("x", 1);""")]
    [Arguments("""await shop.Update("a").Set("x", 1).SendAsync();""")]
    [Arguments("""await Task.Delay(1);""")]
    [Arguments("""await new Mail().SendAsync();""")]
    public async Task Analyze_ChainWithATerminal_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(
            $$"""public static async Task Run() { {{statement}} }""",
            "public sealed class Mail { public Task SendAsync() => Task.CompletedTask; }" + Order);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("await null;")]
    [Arguments("null.SendAsync();")]
    public async Task Analyze_ExpressionWithoutAType_ReportsNothing(string statement)
    {
        // Arrange: code that is still being typed, where the compiler has no type to give.
        var source = AnalyzerRun.Member($$"""public static async Task Run() { {{statement}} }""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_RefStructOfTheConsumerWithTheSameShape_ReportsNothing()
    {
        // Arrange: it fails to compile the same way, but it is not a command of the library.
        var source = AnalyzerRun.Member(
            """public static async Task Run(Reader reader) { new Reader().SendAsync(); await new Reader(); }""",
            "public ref struct Reader { public void ItemsAsync() { } public void ValuesAsync() { } }");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report.Contains("DYNA202", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Analyze_ExtensionOfTheConsumerThatMakesAQueryAwaitable_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            """
            public static ValueTaskAwaiter<int> GetAwaiter<T>(this QueryCommand<T> query)
                where T : IDynamoDbDocument<T> => query.CountAsync().GetAwaiter();

            public static ValueTask<int> SendAsync<T>(this ScanCommand<T> scan)
                where T : IDynamoDbDocument<T> => scan.CountAsync();

            public static async Task Run()
            {
                await shop.Query("a");
                await shop.Scan().SendAsync();
            }
            """);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("QuerySelectCommand<TValue, TConverter>", "where TConverter : IDynamoDbConverter<TValue>", """shop.Query("a").Select(Attr<int>("n"))""")]
    [Arguments("ScanSelectCommand<TValue, TConverter>", "where TConverter : IDynamoDbConverter<TValue>", """shop.Scan().Select(Attr<int>("n"))""")]
    [Arguments("FanOutQueryCommand<T>", "where T : IDynamoDbDocument<T>", """shop.Query(["a", "b"])""")]
    [Arguments("ParallelScanCommand<T>", "where T : IDynamoDbDocument<T>", "shop.Scan().Parallel(8)")]
    [Arguments("MixedQueryCommand<T1, T2>", "where T1 : IDynamoDbDiscriminated<T1> where T2 : IDynamoDbDiscriminated<T2>", """shop.Query("a").Of<Order, Order>()""")]
    [Arguments("MixedScanCommand<T1, T2>", "where T1 : IDynamoDbDiscriminated<T1> where T2 : IDynamoDbDiscriminated<T2>", "shop.Scan().Of<Order, Order>()")]
    public async Task Analyze_ExtensionOfTheConsumerOnAnyRead_ReportsNothing(string receiver, string constraints, string read)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(receiver);
        var arguments = receiver[receiver.IndexOf('<', StringComparison.Ordinal)..];
        var source = AnalyzerRun.Member(
            $$"""
            public static ValueTaskAwaiter<int> GetAwaiter{{arguments}}(this {{receiver}} read)
                {{constraints}} => default(ValueTask<int>).GetAwaiter();

            public static ValueTask<int> SendAsync{{arguments}}(this {{receiver}} read)
                {{constraints}} => default;

            public static async Task Run()
            {
                await {{read}};
                await {{read}}.SendAsync();
            }
            """,
            Order);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Compilation_OfAConsumer_SeesTheInternalMarkerThroughMetadata()
    {
        // Arrange: every test here compiles as a consumer does, against DynaSharp.dll on disk and without InternalsVisibleTo, so this says what the others rest on.
        var compilation = AnalyzerRun.Compile(AnalyzerRun.Member(string.Empty));
        var library = compilation.GetAssemblyOrModuleSymbol(compilation.References.OfType<PortableExecutableReference>().Single(static reference => Path.GetFileName(reference.FilePath) == "DynaSharp.dll"));

        // Act
        var marker = compilation.GetTypeByMetadataName(WellKnownNames.ReadCommandInterface);

        // Assert
        var what = (compilation.Options.MetadataImportOptions, ((IAssemblySymbol)library!).GivesAccessTo(compilation.Assembly), marker?.DeclaredAccessibility, marker?.ContainingAssembly.Name);
        await Assert.That(what).IsEqualTo((MetadataImportOptions.Public, false, Accessibility.Internal, "DynaSharp"));
    }

    [Test]
    public async Task ReadCommand_RefStructsOfTheLibrary_AreTheOnesThatStreamATerminal()
    {
        // Arrange: a read without the marker would lose DYNA202 without a word, because nothing else lists them.
        var commands = typeof(Table<>).Assembly.GetExportedTypes().Where(static type => type is { IsByRefLike: true, Namespace: "DynaSharp" });

        // Act
        var wrong = commands.Where(static type => Streams(type) != typeof(IReadCommand).IsAssignableFrom(type)).Select(static type => type.Name);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Analyze_MethodNamedSendAsyncWithoutAReceiver_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            """
            private static ValueTask SendAsync() => default;

            public static async Task Run() => await SendAsync();
            """);

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CompilerErrorOnTheSameMistake_IsNotSuppressed()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static async Task Run() { await shop.Query("a"); }""");

        // Act
        var errors = AnalyzerRun.CompilerErrors(source);

        // Assert
        await Assert.That(errors.Select(static error => error.Id)).Contains("CS0315");
    }

    private static bool Streams(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Any(static method => method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
}
