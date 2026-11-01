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
    private const string Header = """
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

        public static class Code
        {
            private static readonly Table<Item> shop = null!;

        """;

    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = [new CommandLifetimeAnalyzer(), new ExpressionAnalyzer(), new PathAnalyzer(), new ReadTerminalAnalyzer()];

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

    /// <summary>What the analyzers report, one per line by position: <c>DYNA210 Warning [10..17]: the message</c>.</summary>
    public static async Task<string> ReportAsync(string markedSource, LanguageVersion language = LanguageVersion.Latest)
    {
        var diagnostics = await Compile(markedSource, language).WithAnalyzers(Analyzers).GetAnalyzerDiagnosticsAsync();
        return string.Join("\n", diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).Select(Analysis.Describe));
    }

    /// <summary>What <see cref="ReportAsync"/> gives when the lines, each "id severity: message", land on the marked spans, in order.</summary>
    public static string Expect(string markedSource, params string[] lines) => Analysis.OnSpans(Analysis.Strip(markedSource).Spans, lines);

    /// <summary>The marked spans.</summary>
    public static IReadOnlyList<TextSpan> Marks(string markedSource) => Analysis.Strip(markedSource).Spans;

    /// <summary>The spans of the errors the compiler has for the snippet itself, not for generated code, lowering included, by id: <c>CS4007</c>.</summary>
    public static IReadOnlyList<(string Id, TextSpan Span)> CompilerErrors(string markedSource, LanguageVersion language = LanguageVersion.Latest)
    {
        using var image = new MemoryStream();
        return
        [
            .. Compile(markedSource, language).Emit(image).Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.SourceTree?.FilePath.Length == 0)
                .Select(diagnostic => (diagnostic.Id, diagnostic.Location.SourceSpan)),
        ];
    }
}
