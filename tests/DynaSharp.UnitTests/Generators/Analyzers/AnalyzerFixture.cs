using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// The model of a test class with the output of the generator, compiled once. A test adds its snippet as a tree of its own and has only that tree analyzed, so the compiler binds the bodies of the
/// snippet and not those of the generated files, which are nearly all of what <see cref="AnalyzerRun"/> costs on a model with many entities. A compilation is immutable, so the tests share no state.
/// A snippet is a source of <see cref="AnalyzerRun.Member"/> or <see cref="AnalyzerRun.Statements"/> without the text outside the class. That text comes after the class, so the marks stay on their offsets.
/// </summary>
/// <remarks>
/// <see cref="AnalyzerRun"/> stays the path for what only a whole compilation shows: a compilation-end action, which tree-scoped analysis never runs, and an operation in the model that an analyzer
/// has to have seen when it looks at the snippet, because the model is not analyzed here. The first fails the test. The second cannot fail by itself: a row that needs it would pass for the wrong reason.
/// </remarks>
internal sealed class AnalyzerFixture
{
    /// <summary>A fixture for snippets that need nothing beyond the field <c>shop</c>, which still pay for the references once only.</summary>
    public static readonly AnalyzerFixture NoModel = new(string.Empty);

    private readonly SharedResult<Compilation> _compilation;

    private readonly SharedResult<Compilation> _consumer;

    /// <summary>Initializes a new instance of the <see cref="AnalyzerFixture"/> class, which compiles the model when a test first needs it.</summary>
    /// <param name="model">The text outside the class <c>Code</c>, which sees <see cref="AnalyzerRun.Usings"/>.</param>
    public AnalyzerFixture(string model)
    {
        _compilation = new(() => AnalyzerRun.Compile(AnalyzerRun.Usings + model));

        // A snippet compiled against the fixture instead of inside it, so that emitting it does not emit the generated files too. Only the compiler looks at this one, never an analyzer.
        _consumer = new(() => GeneratorRun.CreateCompilation(string.Empty, LanguageVersion.Latest).WithAssemblyName("Snippet").AddReferences(_compilation.Value.ToMetadataReference()));
    }

    /// <summary>What the analyzers that report while a tree is analyzed report on the snippet, as <see cref="AnalyzerRun.ReportAsync(string, LanguageVersion)"/> says.</summary>
    public Task<string> ReportAsync(string markedSource) => ReportWithAsync(markedSource, [.. AnalyzerRun.TreeAnalyzers]);

    /// <summary>What these analyzers report on the snippet, as <see cref="AnalyzerRun.ReportAsync(string, LanguageVersion)"/> says.</summary>
    public async Task<string> ReportWithAsync(string markedSource, params DiagnosticAnalyzer[] analyzers)
    {
        var snippet = Parse(markedSource);
        var run = _compilation.Value.AddSyntaxTrees(snippet).WithAnalyzers([.. analyzers]);
        await RefuseWhatATreeDoesNotRunAsync(run, analyzers);

        // Not GetAnalyzerSemanticDiagnosticsAsync, which leaves out a diagnostic outside the span of the operation that reports it: DYNA201 on a receiver left of an await, DYNA217 on a retry in another method.
        var result = await run.GetAnalysisResultAsync(run.Compilation.GetSemanticModel(snippet), filterSpan: null, CancellationToken.None);
        return AnalyzerRun.Describe(result.GetAllDiagnostics());
    }

    /// <summary>The spans of the errors the compiler has for the snippet, lowering included, by id: <c>CS4007</c>.</summary>
    public IReadOnlyList<(string Id, TextSpan Span)> CompilerErrors(string markedSource) => AnalyzerRun.ErrorsOfTheSnippet(_consumer.Value.AddSyntaxTrees(Parse(markedSource)));

    // Without this an analyzer of that kind would report nothing here, and a test that expects nothing would pass.
    private static async Task RefuseWhatATreeDoesNotRunAsync(CompilationWithAnalyzers run, DiagnosticAnalyzer[] analyzers)
    {
        foreach (var analyzer in analyzers)
        {
            var actions = await run.GetAnalyzerTelemetryInfoAsync(analyzer, CancellationToken.None);
            if (actions.CompilationEndActionsCount + actions.CompilationActionsCount + actions.SymbolStartActionsCount + actions.SyntaxTreeActionsCount > 0)
            {
                throw new InvalidOperationException($"{analyzer.GetType().Name} has actions that analysis of one tree does not run, so it needs AnalyzerRun.");
            }
        }
    }

    private static SyntaxTree Parse(string markedSource) => CSharpSyntaxTree.ParseText(Analysis.Strip(markedSource).Text, GeneratorRun.ParseOptions(LanguageVersion.Latest));
}
