using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA240, applied in a real workspace, and compiled again to prove the result is what it should be.</summary>
public sealed class IncludeEntitiesCodeFixTests
{
    [Test]
    [Arguments("await orders.CreateTable().SendAsync(ct);", "await orders.CreateTable().Including<Customer>().Including<Product>().SendAsync(ct);")]
    [Arguments("await orders.CreateTable();", "await orders.CreateTable().Including<Customer>().Including<Product>();")]
    [Arguments("await orders.CreateTable().Including<Customer>().IfNotExists().SendAsync(ct);", "await orders.CreateTable().Including<Product>().Including<Customer>().IfNotExists().SendAsync(ct);")]
    [Arguments("_ = client.Table<Order>()?.CreateTable().ToRequestJson();", "_ = client.Table<Order>()?.CreateTable().Including<Customer>().Including<Product>().ToRequestJson();")]
    public async Task Fix_ChainOnOneLine_AppendsTheEntitiesAfterCreateTable(string statement, string expected)
    {
        // Arrange
        var source = LeftOutEntityAnalyzerTests.Source(statement);

        // Act
        var fixedSource = await FixAsync(source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(LeftOutEntityAnalyzerTests.Source(expected));
    }

    [Test]
    public async Task Fix_ChainWithOneCallPerLine_PutsEachEntityOnALineOfItsOwn()
    {
        // Arrange
        var source = LeftOutEntityAnalyzerTests.Source("""
            await orders.CreateTable()
                    .IfNotExists()
                    .SendAsync(ct);
            """);

        // Act
        var fixedSource = await FixAsync(source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(LeftOutEntityAnalyzerTests.Source("""
            await orders.CreateTable()
                    .Including<Customer>()
                    .Including<Product>()
                    .IfNotExists()
                    .SendAsync(ct);
            """));
    }

    [Test]
    public async Task Fix_EntityOfANamespaceTheFileDoesNotImport_NamesItInFull()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            "public static Task Run(DynamoDbClient client) => client.Table<Visit>().CreateTable().SendAsync().AsTask();",
            """[DynamoDbEntity(Table = "bookings"), Discriminator(OptOut = true), PartitionKey("BOOKING#{Id}"), SortKey("AT#{At}")] public partial class Visit { public string Id { get; set; } = "id"; public string At { get; set; } = "at"; }""");

        // Act
        var fixedSource = await FixAsync(source);

        // Assert
        await Assert.That(fixedSource).Contains("client.Table<Visit>().CreateTable().Including<DynaSharp.DefaultsFixture.Booking>().SendAsync()");
    }

    [Test]
    public async Task Fix_Result_HasNoDiagnosticLeftAndCompiles()
    {
        // Arrange
        var source = LeftOutEntityAnalyzerTests.Source("await orders.CreateTable().Including<Product>().SendAsync(ct);");

        // Act
        var fixedSource = await FixAsync(source);
        var report = await AnalyzerRun.ReportWithAsync(fixedSource, new LeftOutEntityAnalyzer());

        // Assert
        await Assert.That((report, AnalyzerRun.CompilerErrors(fixedSource).Count)).IsEqualTo((string.Empty, 0));
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new IncludeEntitiesCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA240"]);
    }

    [Test]
    public async Task GetFixAllProvider_FixesAllInOneGo()
    {
        // Arrange
        var fix = new IncludeEntitiesCodeFix();

        // Act
        var provider = fix.GetFixAllProvider();

        // Assert
        await Assert.That(provider).IsEqualTo(WellKnownFixAllProviders.BatchFixer);
    }

    // The analyzer needs the generated code, which a workspace does not run, so the diagnostic comes from a compilation after the generator, of the same text.
    private static async Task<string> FixAsync(string source)
    {
        ImmutableArray<DiagnosticAnalyzer> analyzers = [new LeftOutEntityAnalyzer()];
        var diagnostic = (await AnalyzerRun.Compile(source).WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync()).Single();

        using var workspace = new AdhocWorkspace();
        var document = workspace.CurrentSolution
            .AddProject("Consumer", "Consumer", LanguageNames.CSharp)
            .WithParseOptions(GeneratorRun.ParseOptions(LanguageVersion.Latest))
            .AddDocument("Code.cs", SourceText.From(source));

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new IncludeEntitiesCodeFix().RegisterCodeFixesAsync(context);

        var operations = await actions.Single().GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var text = await solution.GetDocument(document.Id)!.GetTextAsync();
        return text.ToString();
    }
}
