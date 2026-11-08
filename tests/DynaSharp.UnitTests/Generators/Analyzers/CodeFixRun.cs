using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// Runs a code fix in a real workspace on the diagnostics of one rule: the generator's, from a run over the compilation of the workspace, whose trees are the documents' own, or an analyzer's,
/// over the compilation the generator produced.
/// </summary>
/// <param name="fix">The code fix.</param>
/// <param name="id">The rule.</param>
/// <param name="analyzer">The analyzer that reports the rule, or null for the generator.</param>
/// <param name="nullable">The nullable context of the projects.</param>
internal sealed class CodeFixRun(CodeFixProvider fix, string id, DiagnosticAnalyzer? analyzer = null, NullableContextOptions nullable = NullableContextOptions.Enable)
{
    /// <summary>The diagnostics of the rule by position, in a solution with a project per array, named <c>Consumer0</c>, <c>Consumer1</c> and so on.</summary>
    public async Task<(Solution Solution, ImmutableArray<Diagnostic> Diagnostics)> RunAsync(params (string Path, string Text)[][] projects)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        for (var index = 0; index < projects.Length; index++)
        {
            var project = AddProject(solution, "Consumer" + index, projects[index]);
            solution = project.Solution;
            var found = await ReportAsync(project);
            diagnostics.AddRange(found.Where(diagnostic => diagnostic.Id == id).OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start));
        }

        return (solution, diagnostics.ToImmutable());
    }

    /// <summary>The ids of what the generator, the analyzer and the compiler (its errors) report for the files of one project, after a fix.</summary>
    public async Task<IReadOnlyList<string>> ReportedAsync(Dictionary<string, string> files)
    {
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace.CurrentSolution, "Consumer0", [.. files.Select(file => (file.Key, file.Value))]);
        return [.. (await ReportAsync(project, withCompiler: true)).Select(diagnostic => diagnostic.Id)];
    }

    /// <summary>The actions the fix offers for a diagnostic.</summary>
    public async Task<List<CodeAction>> ActionsAsync(Solution solution, Diagnostic diagnostic)
    {
        var document = solution.GetDocument(diagnostic.Location.SourceTree)!;
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await fix.RegisterCodeFixesAsync(context);
        return actions;
    }

    /// <summary>The titles of the actions offered for each diagnostic, in order.</summary>
    public async Task<List<string>> TitlesAsync(params (string, string)[] files)
    {
        var (solution, diagnostics) = await RunAsync(files);
        var titles = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            titles.AddRange((await ActionsAsync(solution, diagnostic)).Select(action => action.Title));
        }

        return titles;
    }

    /// <summary>The documents the action with <paramref name="key"/> changed for the first diagnostic, by name, with their text.</summary>
    public async Task<Dictionary<string, string>> FixAsync(string key, params (string, string)[] files)
    {
        var (solution, diagnostics) = await RunAsync(files);
        var action = (await ActionsAsync(solution, diagnostics[0])).Single(found => found.EquivalenceKey == key);
        return await ChangesAsync(solution, action, document => document.Name);
    }

    /// <summary>The text of the one file <c>Code.cs</c> after the action with <paramref name="key"/> fixed its first diagnostic.</summary>
    public async Task<string> FixAsync(string key, string source) => (await FixAsync(key, ("Code.cs", source)))["Code.cs"];

    /// <summary>The documents the fix-all changed, by project and name.</summary>
    public async Task<Dictionary<string, string>> FixAllAsync(string key, FixAllScope scope, params (string, string)[][] projects)
    {
        var (solution, diagnostics) = await RunAsync(projects);
        var document = solution.GetDocument(diagnostics[0].Location.SourceTree)!;
        var context = new FixAllContext(document, fix, scope, key, [id], new Diagnostics(diagnostics), CancellationToken.None);
        var action = (await fix.GetFixAllProvider()!.GetFixAsync(context))!;
        return await ChangesAsync(solution, action, changed => changed.Project.Name + "/" + changed.Name);
    }

    private static async Task<Dictionary<string, string>> ChangesAsync(Solution solution, CodeAction action, Func<Document, string> name)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var result = new Dictionary<string, string>();
        foreach (var document in changed.Projects.SelectMany(project => project.Documents))
        {
            var text = (await document.GetTextAsync()).ToString();
            if (text != (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString())
            {
                result[name(document)] = text;
            }
        }

        return result;
    }

    private Project AddProject(Solution solution, string name, (string Path, string Text)[] files)
    {
        var project = solution.AddProject(name, name, LanguageNames.CSharp)
            .WithParseOptions(GeneratorRun.ParseOptions(LanguageVersion.Latest))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable))
            .AddMetadataReferences(GeneratorRun.CreateCompilation(string.Empty, LanguageVersion.Latest).References);
        foreach (var (path, text) in files)
        {
            project = project.AddDocument(path, SourceText.From(text), filePath: name + "/" + path).Project;
        }

        return project;
    }

    private async Task<IEnumerable<Diagnostic>> ReportAsync(Project project, bool withCompiler = false)
    {
        var compilation = (await project.GetCompilationAsync())!;
        GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var found);
        IEnumerable<Diagnostic> all = found;
        if (analyzer is not null)
        {
            all = all.Concat(await output.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync());
        }

        return withCompiler ? all.Concat(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) : all;
    }

    private sealed class Diagnostics(ImmutableArray<Diagnostic> all) : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            var tree = await document.GetSyntaxTreeAsync(cancellationToken);
            return all.Where(diagnostic => diagnostic.Location.SourceTree == tree);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult(all.Where(diagnostic => project.GetDocument(diagnostic.Location.SourceTree) is not null));
    }
}
