using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA210, applied in a real workspace, and compiled again to prove the result is what it should be.</summary>
public sealed class WrapInAttrCodeFixTests
{
    private const string Usings = """
        using System.Threading.Tasks;
        using DynaSharp;

        """;

    private const string StaticImport = "using static DynaSharp.Expr;\n";

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
    public async Task GetFixAllProvider_FixesAllInOneGo()
    {
        // Arrange
        var fix = new WrapInAttrCodeFix();

        // Act
        var provider = fix.GetFixAllProvider();

        // Assert
        await Assert.That(provider).IsEqualTo(WellKnownFixAllProviders.BatchFixer);
    }

    private static string Code(string condition) => $$"""
        public static class Code
        {
            private static readonly Table<Item> shop = null!;

            public static Task Run() => shop.Update("a").If($"{{condition}}").SendAsync().AsTask();
        }

        """;

    private static async Task<string> FixAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var references = GeneratorRun.CreateCompilation(string.Empty, LanguageVersion.Latest).References;
        var document = workspace.CurrentSolution
            .AddProject("Consumer", "Consumer", LanguageNames.CSharp)
            .WithParseOptions(GeneratorRun.ParseOptions(LanguageVersion.Latest))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddMetadataReferences(references)
            .AddDocument("Code.cs", SourceText.From(source));

        var compilation = (await document.Project.GetCompilationAsync())!;
        ImmutableArray<DiagnosticAnalyzer> analyzers = [new ExpressionAnalyzer()];
        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.First(found => found.Id == "DYNA210");

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new WrapInAttrCodeFix().RegisterCodeFixesAsync(context);

        var operations = await actions.Single().GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var text = await solution.GetDocument(document.Id)!.GetTextAsync();
        return text.ToString();
    }
}
