using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// Compiles a snippet against the real DynaSharp.dll, runs the generator and the analyzers over it as a build does, and reads what they report.
/// Spans are marked as in <see cref="Analysis"/>: <c>[|text|]</c>.
/// </summary>
internal static class AnalyzerRun
{
    /// <summary>The usings and the namespace of every source, which a text outside the class <c>Code</c> sees.</summary>
    public const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using DynaSharp;
        using DynaSharp.Expressions;
        using DynaSharp.Mapping;
        using DynaSharp.Serialization;
        using static DynaSharp.Expr;

        namespace Shop;

        """;

    /// <summary>The analyzers that <see cref="AnalyzerFixture"/> can run. <see cref="DiscriminatorAnalyzer"/> is not one: it reports when a compilation ends.</summary>
    public static readonly ImmutableArray<DiagnosticAnalyzer> TreeAnalyzers = [new CommandLifetimeAnalyzer(), new ExpressionAnalyzer(), new HandleOwnerAnalyzer(), new PathAnalyzer(), new ReadTerminalAnalyzer(), new TypeArgumentAnalyzer()];

    private const string Header = Usings + """

        public static class Code
        {
            private static readonly Table<Item> shop = null!;

        """;

    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = [.. TreeAnalyzers, new DiscriminatorAnalyzer()];

    /// <summary>A whole source with this member in the class <c>Code</c>, which has a field <c>shop</c> of type <c>Table&lt;Item&gt;</c>.</summary>
    public static string Member(string member, string outside = "") => Header + member + "\n}\n" + outside;

    /// <summary>A whole source with these statements in a method of the class <c>Code</c>.</summary>
    public static string Statements(string statements, string outside = "") => Member("public static void Run()\n{\n" + statements + "\n}\n", outside);

    /// <summary>The compilation as the compiler sees it after the generator ran.</summary>
    public static Compilation Compile(string markedSource, LanguageVersion language = LanguageVersion.Latest)
    {
        var (text, _) = Analysis.Strip(markedSource);
        var compilation = GeneratorRun.CreateCompilation(text, language);
        GeneratorRun.CreateDriver(new DynaSharpGenerator(), language).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }

    /// <summary>
    /// What the analyzers report, one per line by position: <c>DYNA210 Warning [10..17]: the message</c>. <see cref="DiscardedCommandAnalyzer"/> is left out: a snippet is a statement whose value
    /// nothing uses, so it would report nearly every snippet of the other rules. <see cref="ReportWithAsync"/> runs it.
    /// </summary>
    public static Task<string> ReportAsync(string markedSource, LanguageVersion language = LanguageVersion.Latest) => ReportAsync(markedSource, language, Analyzers);

    /// <summary>What these analyzers report, as <see cref="ReportAsync(string, LanguageVersion)"/> says.</summary>
    public static Task<string> ReportWithAsync(string markedSource, params DiagnosticAnalyzer[] analyzers) => ReportAsync(markedSource, LanguageVersion.Latest, [.. analyzers]);

    /// <summary>What <see cref="ReportAsync(string, LanguageVersion)"/> gives when the lines, each "id severity: message", land on the marked spans, in order.</summary>
    public static string Expect(string markedSource, params string[] lines) => Analysis.OnSpans(Analysis.Strip(markedSource).Spans, lines);

    /// <summary>The marked spans.</summary>
    public static IReadOnlyList<TextSpan> Marks(string markedSource) => Analysis.Strip(markedSource).Spans;

    /// <summary>The spans of the errors the compiler has for the snippet itself, not for generated code, lowering included, by id: <c>CS4007</c>.</summary>
    public static IReadOnlyList<(string Id, TextSpan Span)> CompilerErrors(string markedSource, LanguageVersion language = LanguageVersion.Latest) => ErrorsOfTheSnippet(Compile(markedSource, language));

    /// <summary>What a compilation that holds a snippet gives when it is emitted, for the trees that have no file, which the generated ones have.</summary>
    public static IReadOnlyList<(string Id, TextSpan Span)> ErrorsOfTheSnippet(Compilation compilation)
    {
        using var image = new MemoryStream();
        return
        [
            .. compilation.Emit(image).Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.SourceTree?.FilePath.Length == 0)
                .Select(diagnostic => (diagnostic.Id, diagnostic.Location.SourceSpan)),
        ];
    }

    /// <summary>The diagnostics as a report: one per line, by position.</summary>
    public static string Describe(ImmutableArray<Diagnostic> diagnostics) => string.Join("\n", diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).Select(Analysis.Describe));

    private static async Task<string> ReportAsync(string markedSource, LanguageVersion language, ImmutableArray<DiagnosticAnalyzer> analyzers) =>
        Describe(await Compile(markedSource, language).WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync());
}
