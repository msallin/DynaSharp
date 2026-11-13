using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA220 and of the DYNA212 of a name starting with an underscore, applied in a real workspace to what the analyzer reports there, and analyzed and compiled again.</summary>
public sealed class ReservedWordCodeFixTests
{
    private const string Key = ReservedWordCodeFix.Key;

    private const string Usings = "using System.Threading.Tasks;\nusing DynaSharp;\n";

    private const string StaticImport = "using static DynaSharp.Expr;\n";

    private static readonly CodeFixRun Run = new(new ReservedWordCodeFix(), "DYNA220", new ExpressionAnalyzer());

    private static readonly CodeFixRun SyntaxRun = new(new ReservedWordCodeFix(), "DYNA212", new ExpressionAnalyzer());

    [Test]
    [Arguments("$\"Total > {100}\"", "$\"{Attr(\"Total\")} > {100}\"")]
    [Arguments("$@\"Total > {100}\"", "$@\"{Attr(\"Total\")} > {100}\"")]
    [Arguments("$\"\"\"total > {100}\"\"\"", "$\"\"\"{Attr(\"total\")} > {100}\"\"\"")]
    [Arguments("$$\"\"\"total > {{100}}\"\"\"", "$$\"\"\"{{Attr(\"total\")}} > {{100}}\"\"\"")]
    [Arguments("$\"info.year > {1}\"", "$\"info.{Attr(\"year\")} > {1}\"")]
    public async Task Fix_FileWithTheImport_PutsTheWordInAHole(string filter, string expected)
    {
        // Arrange
        var source = Usings + StaticImport + Code(filter);

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Usings + StaticImport + Code(expected));
    }

    [Test]
    public async Task Fix_FileWithoutTheImport_AddsIt()
    {
        // Arrange
        var source = Usings + Code("$\"Total > {100}\"");

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Usings + StaticImport + Code("$\"{Attr(\"Total\")} > {100}\""));
    }

    [Test]
    [Arguments("$\"Total > {100}\"")]
    [Arguments("$\"info.year > {1}\"")]
    public async Task Fix_Result_HasNothingLeftToReport(string filter)
    {
        // Arrange
        var source = Usings + Code(filter);

        // Act
        var fixedFiles = await Run.FixAsync(Key, ("Code.cs", source));
        var reported = await Run.ReportedAsync(fixedFiles);

        // Assert
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    [Arguments("$\"_type = {\"Order\"}\"", "$\"{Attr(\"_type\")} = {\"Order\"}\"")]
    [Arguments("$\"info._v2 > {1}\"", "$\"info.{Attr(\"_v2\")} > {1}\"")]
    public async Task Fix_NameStartingWithAnUnderscore_PutsItInAHole(string filter, string expected)
    {
        // Arrange
        var source = Usings + StaticImport + Code(filter);

        // Act
        var fixedSource = await SyntaxRun.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Usings + StaticImport + Code(expected));
    }

    [Test]
    [Arguments("$\"price = {1} price\"")]
    [Arguments("$\"price = \"")]
    public async Task RegisterCodeFixes_SyntaxErrorOnAnotherWord_OffersNothing(string filter)
    {
        // Arrange
        var source = Usings + Code(filter);

        // Act
        var (solution, diagnostics) = await SyntaxRun.RunAsync([("Code.cs", source)]);
        var actions = await SyntaxRun.ActionsAsync(solution, diagnostics.Single());

        // Assert
        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Word_NamesTheHole()
    {
        // Arrange
        var source = Usings + Code("$$\"\"\"total > {{100}}\"\"\"");

        // Act
        var titles = await Run.TitlesAsync(("Code.cs", source));

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Write {{Attr(\"total\")}}"]);
    }

    [Test]
    public async Task RegisterCodeFixes_TextWithAnEscape_OffersNothing()
    {
        // Arrange
        var source = Usings + Code("$\"\\tTotal > {100}\"");

        // Act
        var (solution, diagnostics) = await Run.RunAsync([("Code.cs", source)]);
        var actions = await Run.ActionsAsync(solution, diagnostics.Single());

        // Assert
        await Assert.That(actions).IsEmpty();
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    public async Task FixAll_TwoWordsInAFileWithoutTheImport_PutsEachInAHoleAndAddsTheImportOnce(FixAllScope scope)
    {
        // Arrange
        var source = Usings + Code("$\"Total > {1} AND Count > {2}\"");

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, scope, [("Code.cs", source)]);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Code.cs"]).IsEqualTo(Usings + StaticImport + Code("$\"{Attr(\"Total\")} > {1} AND {Attr(\"Count\")} > {2}\""));
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new ReservedWordCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA220", "DYNA212"]);
    }

    // With the line ends of the usings, which the import takes.
    private static string Code(string filter) => ("""

        public static class Code
        {
            private static readonly Table<Item> shop = null!;

            public static void Run() => shop.Scan().Filter(
        """ + filter + ");\n}\n").ReplaceLineEndings("\n");
}
