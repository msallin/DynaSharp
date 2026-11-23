using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The edit of DYNA234, applied in a real workspace to what <see cref="ReadTerminalAnalyzer"/> reports there, and analyzed again.</summary>
public sealed class SuggestedEditCodeFixReadTests
{
    private const string Items = "DYNA234.0";

    private static readonly CodeFixRun Fixture = new(new SuggestedEditCodeFix(), analyzer: new ReadTerminalAnalyzer()) { Model = string.Empty };

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

    private static string Run(string statements) => AnalyzerRun.Member("public static async Task Run()\n{\n" + statements + "\n}\n");
}
