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
/// Runs a code fix in a real workspace on the diagnostics of its rules: the generator's, from a run over the compilation of the workspace, whose trees are the documents' own, and an analyzer's,
/// over the compilation the generator produced. Each project is named <c>Consumer0</c>, <c>Consumer1</c> and so on, and each document <c>ConsumerN/Name.cs</c>. A snippet (a single file,
/// <c>Code.cs</c>) may carry the marks of <see cref="Analysis.Strip"/>, which are taken out.
/// </summary>
/// <param name="fix">The code fix.</param>
/// <param name="id">The rule, or null for every rule the fix fixes.</param>
/// <param name="analyzer">The analyzer that reports the rule, or null for the generator alone.</param>
/// <param name="nullable">The nullable context of the projects.</param>
internal sealed class CodeFixRun(CodeFixProvider fix, string? id = null, DiagnosticAnalyzer? analyzer = null, NullableContextOptions nullable = NullableContextOptions.Enable)
{
    /// <summary>The usings and namespace that <see cref="One"/> puts before the declarations.</summary>
    public const string Header = "using DynaSharp.Mapping;\n\nnamespace Shop;\n\n";

    private const string Snippet = "Code.cs";

    private readonly ImmutableArray<string> _rules = id is null ? fix.FixableDiagnosticIds : [id];

    private SharedResult<ImmutableArray<MetadataReference>>? _model;

    /// <summary>Gets the libraries each project references, such as one with a base type in another assembly (<see cref="Analysis.Library"/>).</summary>
    public MetadataReference[] Libraries { get; init; } = [];

    /// <summary>Gets a value indicating whether each project references the one before it, whose types then have source that is not in the compilation.</summary>
    public bool Chained { get; init; }

    /// <summary>
    /// Sets the text outside the class <c>Code</c> that the snippets of <see cref="AnalyzerRun.Member"/> and <see cref="AnalyzerRun.Statements"/> use, which sees <see cref="AnalyzerRun.Usings"/>. It is
    /// compiled once with the output of the generator and referenced as an assembly, as a project references another, so the workspace binds a snippet as an IDE does.
    /// </summary>
    public string Model
    {
        init => _model = new(() => CompileModel(value));
    }

    /// <summary>One file, <c>Order.cs</c>, of <see cref="Header"/> and the declarations.</summary>
    public static (string, string)[] One(string declarations) => [("Order.cs", Header + declarations)];

    /// <summary>The diagnostics of the rules, by file and position, in a solution with a project per array.</summary>
    public async Task<(Solution Solution, ImmutableArray<Diagnostic> Diagnostics)> RunAsync(params (string Path, string Text)[][] projects)
    {
        var (solution, diagnostics, _) = await CompileAsync(projects);
        return (solution, [.. diagnostics.Where(diagnostic => _rules.Contains(diagnostic.Id))]);
    }

    /// <summary>The ids of what the generator, the analyzer and the compiler (its errors) report for the files of one project, after a fix.</summary>
    public async Task<IReadOnlyList<string>> ReportedAsync(Dictionary<string, string> files)
    {
        var (_, diagnostics, errors) = await CompileAsync([[.. files.Select(file => (file.Key, file.Value))]]);
        return [.. diagnostics.Concat(errors).Select(diagnostic => diagnostic.Id)];
    }

    /// <summary>The ids of the errors and of the diagnostics of the rules left once the fix changed some of the files.</summary>
    public async Task<List<string>> LeftAsync(Dictionary<string, string> fixedFiles, (string Path, string Text)[] files)
    {
        (string, string)[] all = [.. files.Select(file => (file.Path, fixedFiles.TryGetValue(file.Path, out var text) ? text : file.Text))];
        var (_, diagnostics, errors) = await CompileAsync([all]);
        return [.. diagnostics.Concat(errors).Where(diagnostic => _rules.Contains(diagnostic.Id) || diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id)];
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

    /// <summary>The titles of the actions offered for each diagnostic of a snippet, in order.</summary>
    public Task<List<string>> TitlesAsync(string source) => TitlesAsync((Snippet, Analysis.Strip(source).Text));

    /// <summary>The documents the action with <paramref name="key"/> changed for the first diagnostic, by name, with their text.</summary>
    public async Task<Dictionary<string, string>> FixAsync(string key, params (string, string)[] files)
    {
        var (solution, diagnostics) = await RunAsync(files);
        var action = (await ActionsAsync(solution, diagnostics[0])).Single(found => found.EquivalenceKey == key);
        return await ChangesAsync(solution, action, document => document.Name);
    }

    /// <summary>The text of a snippet after the action with <paramref name="key"/> fixed its first diagnostic.</summary>
    public async Task<string> FixAsync(string key, string source)
    {
        var text = Analysis.Strip(source).Text;
        return (await FixAsync(key, (Snippet, text))).GetValueOrDefault(Snippet, text);
    }

    /// <summary>The documents the fix-all changed, by project and name.</summary>
    public async Task<Dictionary<string, string>> FixAllAsync(string key, FixAllScope scope, params (string, string)[][] projects)
    {
        var (solution, diagnostics) = await RunAsync(projects);
        var document = solution.GetDocument(diagnostics[0].Location.SourceTree)!;
        var context = new FixAllContext(document, fix, scope, key, _rules, new Diagnostics(diagnostics), CancellationToken.None);
        var action = (await fix.GetFixAllProvider()!.GetFixAsync(context))!;
        return await ChangesAsync(solution, action, changed => changed.Project.Name + "/" + changed.Name);
    }

    /// <summary>The text of a snippet after the fix-all with <paramref name="key"/> over every diagnostic of the rules in it.</summary>
    public async Task<string> FixAllAsync(string key, FixAllScope scope, string source)
    {
        var text = Analysis.Strip(source).Text;
        return (await FixAllAsync(key, scope, [(Snippet, text)])).GetValueOrDefault("Consumer0/" + Snippet, text);
    }

    /// <summary>What the rules report on a snippet, as <see cref="AnalyzerRun.Describe"/> writes it: empty once the fix did its work.</summary>
    public async Task<string> ReportAsync(string source) => AnalyzerRun.Describe((await RunAsync([(Snippet, Analysis.Strip(source).Text)])).Diagnostics);

    /// <summary>The errors the compiler has for a snippet, by id.</summary>
    public async Task<List<string>> CompilerErrorsAsync(string source) => [.. (await CompileAsync([[(Snippet, Analysis.Strip(source).Text)]])).Errors.Select(diagnostic => diagnostic.Id)];

    private static ImmutableArray<MetadataReference> CompileModel(string model)
    {
        var compilation = AnalyzerRun.Compile(AnalyzerRun.Usings + model);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("The model does not compile: " + string.Join("; ", emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }

        return [MetadataReference.CreateFromImage(image.ToArray())];
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

    // What the generator and the analyzer report, by file and position, and the errors of the compilation with the generated code in it.
    private async Task<(Solution Solution, List<Diagnostic> Diagnostics, List<Diagnostic> Errors)> CompileAsync((string Path, string Text)[][] projects)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var references = GeneratorRun.CreateCompilation(string.Empty, LanguageVersion.Latest).References;
        var diagnostics = new List<Diagnostic>();
        var errors = new List<Diagnostic>();
        for (var index = 0; index < projects.Length; index++)
        {
            var name = "Consumer" + index;
            var project = solution.AddProject(name, name, LanguageNames.CSharp)
                .WithParseOptions(GeneratorRun.ParseOptions(LanguageVersion.Latest))
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable))
                .AddMetadataReferences(references)
                .AddMetadataReferences(Libraries)
                .AddMetadataReferences(_model?.Value ?? []);
            if (Chained && index > 0)
            {
                project = project.AddProjectReference(new ProjectReference(solution.Projects.Single(other => other.Name == "Consumer" + (index - 1)).Id));
            }

            foreach (var (path, text) in projects[index])
            {
                project = project.AddDocument(path, SourceText.From(text), filePath: name + "/" + path).Project;
            }

            solution = project.Solution;
            var compilation = (await project.GetCompilationAsync())!;
            GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var found);
            var reported = found.AsEnumerable();
            if (analyzer is not null)
            {
                reported = reported.Concat(await output.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync());
            }

            diagnostics.AddRange(reported.OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start));
            errors.AddRange(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }

        return (solution, diagnostics, errors);
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
