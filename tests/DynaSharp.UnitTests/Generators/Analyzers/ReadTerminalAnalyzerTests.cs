namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA202: a query or scan that is awaited or sent, where the compiler only has a constraint to complain about.</summary>
public sealed class ReadTerminalAnalyzerTests
{
    private const string Message = "DYNA202 Warning: Query and Scan are not awaited; end the chain with ItemsAsync, PageAsync, PagesAsync or CountAsync";

    [Test]
    [Arguments("""[|await shop.Query("a")|];""")]
    [Arguments("""[|await shop.Scan()|];""")]
    [Arguments("""[|await shop.Query("a").Limit(1)|];""")]
    [Arguments("""[|await shop.Scan().Parallel(8)|];""")]
    [Arguments("""[|await (shop.Query("a"))|];""")]
    public async Task Analyze_AwaitOfAReadCommand_ReportsTheAwait(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member($$"""public static async Task Run() { {{statement}} }""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""shop.Query("a").[|SendAsync|]();""")]
    [Arguments("""shop.Scan().[|SendAsync|](CancellationToken.None);""")]
    [Arguments("""shop.Query("a").Filter(Attr("x").Exists()).[|SendAsync|]();""")]
    public async Task Analyze_SendOfAReadCommand_ReportsTheName(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Statements(statement);

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
    [Arguments("""await shop.Get("a");""")]
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
            "public sealed class Mail { public Task SendAsync() => Task.CompletedTask; }");

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
            "public ref struct Reader { public void ItemsAsync() { } }");

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
}
