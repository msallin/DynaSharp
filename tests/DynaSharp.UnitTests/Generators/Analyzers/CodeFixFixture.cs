using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// A code fix of an analyzer, applied in a real workspace to what the analyzer reports there. The model of a test class, with the output of the generator, is compiled once and referenced as an
/// assembly, as a project references another, so the workspace binds a snippet as an IDE does, generated members included, without running the generator. A snippet is a source of
/// <see cref="AnalyzerRun.Member"/> or <see cref="AnalyzerRun.Statements"/>, marks stripped.
/// </summary>
/// <param name="model">The text outside the class <c>Code</c> that the snippets use, which sees <see cref="AnalyzerRun.Usings"/>.</param>
/// <param name="analyzer">The analyzer whose diagnostics the fix takes.</param>
/// <param name="fix">The fix.</param>
internal sealed class CodeFixFixture(string model, DiagnosticAnalyzer analyzer, CodeFixProvider fix)
{
    private readonly SharedResult<ImmutableArray<MetadataReference>> _references = new(() =>
    {
        var compilation = AnalyzerRun.Compile(AnalyzerRun.Usings + model);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("The model does not compile: " + string.Join("; ", emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }

        return [.. compilation.References, MetadataReference.CreateFromImage(image.ToArray())];
    });

    /// <summary>The titles of the fixes of the first diagnostic of the fix's rules in the snippet.</summary>
    public async Task<List<string>> TitlesAsync(string source)
    {
        var (document, diagnostics) = await AnalyzeAsync(source);
        return [.. (await ActionsAsync(document, diagnostics[0])).Select(action => action.Title)];
    }

    /// <summary>The snippet after the fix with this equivalence key of its first diagnostic.</summary>
    public async Task<string> FixAsync(string source, string key)
    {
        var (document, diagnostics) = await AnalyzeAsync(source);
        var action = (await ActionsAsync(document, diagnostics[0])).Single(found => found.EquivalenceKey == key);
        return await ChangedTextAsync(document, action);
    }

    /// <summary>The snippet after the fix-all of the fix with this equivalence key, over every diagnostic of the fix's rules in it.</summary>
    public async Task<string> FixAllAsync(string source, string key, FixAllScope scope)
    {
        var (document, diagnostics) = await AnalyzeAsync(source);
        var context = new FixAllContext(document, fix, scope, key, fix.FixableDiagnosticIds, new Diagnostics(diagnostics), CancellationToken.None);
        var action = (await fix.GetFixAllProvider()!.GetFixAsync(context))!;
        return await ChangedTextAsync(document, action);
    }

    /// <summary>What the analyzer reports on the snippet, of the fix's rules, as <see cref="AnalyzerRun.Describe"/> writes it: empty once the fix did its work.</summary>
    public async Task<string> ReportAsync(string source) => AnalyzerRun.Describe((await AnalyzeAsync(source)).Diagnostics);

    /// <summary>The errors the compiler has for the snippet, by id.</summary>
    public async Task<List<string>> CompilerErrorsAsync(string source)
    {
        var compilation = (await Project(source).GetCompilationAsync())!;
        return [.. compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id)];
    }

    private static async Task<string> ChangedTextAsync(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        return (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }

    private Project Project(string source)
    {
        using var workspace = new AdhocWorkspace();
        return workspace.CurrentSolution
            .AddProject("Consumer", "Consumer", LanguageNames.CSharp)
            .WithParseOptions(GeneratorRun.ParseOptions(LanguageVersion.Latest))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddMetadataReferences(_references.Value)
            .AddDocument("Code.cs", SourceText.From(Analysis.Strip(source).Text), filePath: "Code.cs")
            .Project;
    }

    private async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> AnalyzeAsync(string source)
    {
        var document = Project(source).Documents.Single();
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync();
        return (document, [.. diagnostics.Where(diagnostic => fix.FixableDiagnosticIds.Contains(diagnostic.Id)).OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)]);
    }

    private async Task<List<CodeAction>> ActionsAsync(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await fix.RegisterCodeFixesAsync(context);
        return actions;
    }

    private sealed class Diagnostics(ImmutableArray<Diagnostic> all) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(all);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(all);
    }
}
