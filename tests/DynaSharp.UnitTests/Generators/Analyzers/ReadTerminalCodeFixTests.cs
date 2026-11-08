using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA202 and DYNA234, applied in a real workspace to what <see cref="ReadTerminalAnalyzer"/> reports there, and analyzed again.</summary>
public sealed class ReadTerminalCodeFixTests
{
    private const string Items = ReadTerminalCodeFix.ItemsKey;

    private static readonly CodeFixRun Fixture = new(new ReadTerminalCodeFix(), analyzer: new ReadTerminalAnalyzer()) { Model = string.Empty };

    [Test]
    [Arguments("""await shop.Query("a");""", new[] { "ItemAsync()", "PageAsync()", "CountAsync()" })]
    [Arguments("""await shop.Query("a").SendAsync().ConfigureAwait(false);""", new[] { "ItemAsync()", "PageAsync()", "CountAsync()" })]
    [Arguments("""await foreach (var item in shop.Query("a").SendAsync()) { }""", new[] { "ItemsAsync()", "PagesAsync()" })]
    [Arguments("""await foreach (var item in shop.Query("a").SendAsync().ConfigureAwait(false)) { }""", new[] { "ItemsAsync()", "PagesAsync()" })]
    [Arguments("""_ = shop.Scan().SendAsync(CancellationToken.None);""", new[] { "ItemsAsync(CancellationToken.None)", "PagesAsync(CancellationToken.None)", "PageAsync(CancellationToken.None)", "CountAsync(CancellationToken.None)" })]
    [Arguments("""_ = shop.Query("a").SendAsync().AsTask();""", new[] { "ItemAsync()", "PageAsync()", "CountAsync()" })]
    [Arguments("""await shop.Scan().Parallel(8);""", new string[0])]
    [Arguments("""_ = shop.Query(["a", "b"]).SendAsync();""", new[] { "ItemsAsync()", "CountAsync()" })]
    [Arguments("""await shop.Query("a").Select(Attr<int>("n"));""", new[] { "ValueAsync()", "PageAsync()" })]
    public async Task Titles_Read_OfferEachTerminalThatFitsWhereTheReadStands(string statement, string[] terminals)
    {
        // Arrange (a fan-out has a PageAsync that takes a page size, which is the author's to choose)
        var source = Run(statement);

        // Act
        var titles = await Fixture.TitlesAsync(source);

        // Assert
        await Assert.That(titles).IsEquivalentTo(terminals.Select(terminal => "End the read with " + terminal));
    }

    [Test]
    [Arguments("""await shop.Query("a");""", "ItemAsync", """await shop.Query("a").ItemAsync();""")]
    [Arguments("""await (shop.Query("a"));""", "CountAsync", """await (shop.Query("a")).CountAsync();""")]
    [Arguments("""_ = shop.Scan().SendAsync(CancellationToken.None);""", "ItemsAsync", """_ = shop.Scan().ItemsAsync(CancellationToken.None);""")]
    [Arguments("""await foreach (var item in shop.Query("a").SendAsync()) { }""", "PagesAsync", """await foreach (var item in shop.Query("a").PagesAsync()) { }""")]
    public async Task Fix_Terminal_EndsTheReadWithIt(string statement, string terminal, string expected)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var fixedSource = await Fixture.FixAsync(ReadTerminalCodeFix.TerminalKey + terminal, source);

        // Assert
        await Assert.That((fixedSource, await Fixture.ReportAsync(fixedSource), (await Fixture.CompilerErrorsAsync(fixedSource)).Count)).IsEqualTo((Run(expected), string.Empty, 0));
    }

    [Test]
    public async Task Fix_ReadOnLinesOfTheirOwn_PutsTheTerminalOnALineOfItsOwn()
    {
        // Arrange
        var source = Run("""
            await shop.Query("a")
                .Limit(5);
            """);

        // Act
        var fixedSource = await Fixture.FixAsync(ReadTerminalCodeFix.TerminalKey + "ItemAsync", source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Run("""
            await shop.Query("a")
                .Limit(5).ItemAsync();
            """));
    }

    [Test]
    [Arguments("""_ = await shop.Query("a").ToListAsync();""", """_ = await shop.Query("a").ItemsAsync().ToListAsync();""")]
    [Arguments("""_ = await shop.Query("a").Select(Attr<int>("n")).Take(1).ToListAsync();""", """_ = await shop.Query("a").Select(Attr<int>("n")).ValuesAsync().Take(1).ToListAsync();""")]
    public async Task Fix_LinqMethodOnARead_CallsTheTerminalBeforeIt(string statement, string expected)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var fixedSource = await Fixture.FixAsync(Items, source);

        // Assert
        await Assert.That((fixedSource, await Fixture.ReportAsync(fixedSource), (await Fixture.CompilerErrorsAsync(fixedSource)).Count)).IsEqualTo((Run(expected), string.Empty, 0));
    }

    [Test]
    public async Task Fix_LinqMethodOnAReadOnLinesOfTheirOwn_PutsTheTerminalOnALineOfItsOwn()
    {
        // Arrange
        var source = Run("""
            _ = await shop.Query("a")
                .ToListAsync();
            """);

        // Act
        var fixedSource = await Fixture.FixAsync(Items, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Run("""
            _ = await shop.Query("a")
                .ItemsAsync()
                .ToListAsync();
            """));
    }

    [Test]
    public async Task Titles_LinqMethodOnARead_NameTheTerminalAndTheMethod()
    {
        // Arrange
        var source = Run("""_ = await shop.Query("a").Select(Attr<int>("n")).FirstAsync();""");

        // Act
        var titles = await Fixture.TitlesAsync(source);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Call ValuesAsync() before FirstAsync"]);
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    public async Task FixAll_Terminal_EndsEveryReadThatHasIt(FixAllScope scope)
    {
        // Arrange (a parallel scan has no CountAsync, and stays as it was)
        var source = Run("""await shop.Query("a"); await shop.Scan(); await shop.Scan().Parallel(2);""");

        // Act
        var fixedSource = await Fixture.FixAllAsync(ReadTerminalCodeFix.TerminalKey + "CountAsync", scope, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Run("""await shop.Query("a").CountAsync(); await shop.Scan().CountAsync(); await shop.Scan().Parallel(2);"""));
    }

    [Test]
    public async Task FixableDiagnosticIds_AreTheRules()
    {
        // Arrange
        var fix = new ReadTerminalCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA202", "DYNA234"]);
    }

    [Test]
    public async Task GetFixAllProvider_FixesAllInOneGo()
    {
        // Arrange
        var fix = new ReadTerminalCodeFix();

        // Act
        var provider = fix.GetFixAllProvider();

        // Assert
        await Assert.That(provider).IsEqualTo(WellKnownFixAllProviders.BatchFixer);
    }

    private static string Run(string statements) => AnalyzerRun.Member("public static async Task Run()\n{\n" + statements + "\n}\n");
}
