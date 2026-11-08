using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA210, applied in a real workspace, and compiled again to prove the result is what it should be.</summary>
public sealed class WrapInAttrCodeFixTests
{
    private const string Usings = """
        using System.Threading.Tasks;
        using DynaSharp;

        """;

    private const string StaticImport = "using static DynaSharp.Expr;\n";

    private static readonly CodeFixRun Run = new(new WrapInAttrCodeFix(), "DYNA210", new ExpressionAnalyzer());

    [Test]
    public async Task Fix_FileWithTheImport_WrapsTheString()
    {
        // Arrange
        var source = Usings + StaticImport + "\n" + Code("""{"status"} = {"open"}""");

        // Act
        var fixedSource = await FixAsync(source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Usings + StaticImport + "\n" + Code("""{Attr("status")} = {"open"}"""));
    }

    [Test]
    public async Task Fix_FileWithoutTheImport_AddsIt()
    {
        // Arrange
        var source = Usings + "\n" + Code("""{"status"} = {"open"}""");

        // Act
        var fixedSource = await FixAsync(source);

        // Assert
        await Assert.That(fixedSource).Contains("using static DynaSharp.Expr;");
        await Assert.That(fixedSource).Contains("""{Attr("status")} = {"open"}""");
    }

    [Test]
    public async Task Fix_Result_HasNoDiagnosticLeftAndCompiles()
    {
        // Arrange
        var source = Usings + "\n" + Code("""attribute_exists({"pk"})""");

        // Act
        var fixedSource = await FixAsync(source);
        var report = await AnalyzerRun.ReportAsync(fixedSource);

        // Assert
        await Assert.That(report).IsEmpty();
        await Assert.That(AnalyzerRun.CompilerErrors(fixedSource)).IsEmpty();
    }

    [Test]
    public async Task Fix_NamedConstant_WrapsTheName()
    {
        // Arrange
        var source = Usings + StaticImport + """

            public static class Code
            {
                private const string Status = "status";
                private static readonly Table<Item> shop = null!;

                public static Task Run() => shop.Update("a").If($"{Status} = {1}").SendAsync().AsTask();
            }

            """;

        // Act
        var fixedSource = await FixAsync(source);

        // Assert
        await Assert.That(fixedSource).Contains("""{Attr(Status)} = {1}""");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new WrapInAttrCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA210"]);
    }

    [Test]
    public async Task FixAll_FileWithoutTheImport_WrapsEachAndAddsTheImportOnce()
    {
        // Arrange
        var source = Usings + "\n" + Code("""{"status"} = {"open"} AND {"total"} > {1}""");

        // Act
        var fixedSource = await Run.FixAllAsync("Wrap in Attr(...)", FixAllScope.Document, source);

        // Assert
        await Assert.That((fixedSource.Split("using static DynaSharp.Expr;").Length, fixedSource.Contains("""{Attr("status")} = {"open"} AND {Attr("total")} > {1}""", StringComparison.Ordinal))).IsEqualTo((2, true));
    }

    private static string Code(string condition) => $$"""
        public static class Code
        {
            private static readonly Table<Item> shop = null!;

            public static Task Run() => shop.Update("a").If($"{{condition}}").SendAsync().AsTask();
        }

        """;

    private static Task<string> FixAsync(string source) => Run.FixAsync("Wrap in Attr(...)", source);
}
