using System.Text;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// Runs the real generator over a piece of source and reads what it reported or modelled. Spans are marked in the source as
/// <c>[|text|]</c>, so a test states the span where it happens instead of counting offsets.
/// </summary>
internal static class Analysis
{
    private const string Open = "[|";

    private const string Close = "|]";

    private const string Header = """
        using System;
        using System.Collections.Generic;
        using DynaSharp;
        using DynaSharp.Mapping;
        using DynaSharp.Serialization;

        namespace Shop;


        """;

    /// <summary>Every diagnostic of a run, one per line, in the order reported: <c>DYNA001 Error [10..17]: the message</c>.</summary>
    public static string Report(string markedSource, LanguageVersion language = LanguageVersion.Latest, params MetadataReference[] references)
    {
        var (text, _) = Strip(Header + markedSource);
        var compilation = GeneratorRun.CreateCompilation(text, language).AddReferences(references);
        var result = GeneratorRun.CreateDriver(new DynaSharpGenerator(), language).RunGenerators(compilation).GetRunResult();
        return string.Join("\n", result.Diagnostics.Select(Describe));
    }

    /// <summary>What <see cref="Report"/> gives when <paramref name="otherFile"/>, which has no header, is a second file of the compilation.</summary>
    public static string ReportWithFile(string markedSource, string otherFile)
    {
        var other = CSharpSyntaxTree.ParseText(Strip(otherFile).Text, GeneratorRun.ParseOptions(LanguageVersion.Latest), "Other.cs");
        var compilation = GeneratorRun.CreateCompilation(Strip(Header + markedSource).Text, LanguageVersion.Latest).AddSyntaxTrees(other);
        var result = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGenerators(compilation).GetRunResult();
        return string.Join("\n", result.Diagnostics.Select(Describe));
    }

    /// <summary>What <see cref="Report"/> gives for a source with its own usings and namespaces, which is where an assembly attribute can go.</summary>
    public static string ReportOfWholeFile(string markedSource)
    {
        var compilation = GeneratorRun.CreateCompilation(Strip(markedSource).Text, LanguageVersion.Latest);
        var result = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGenerators(compilation).GetRunResult();
        return string.Join("\n", result.Diagnostics.Select(Describe));
    }

    /// <summary>What <see cref="ReportOfWholeFile"/> gives when the lines, each "id severity: message", land on the marked spans, in order.</summary>
    public static string ExpectOfWholeFile(string markedSource, params string[] lines) => OnSpans(Strip(markedSource).Spans, lines);

    /// <summary>What <see cref="Report"/> gives when the lines, each "id severity: message", land on the marked spans, in order.</summary>
    public static string Expect(string markedSource, params string[] lines) => OnSpans(Strip(Header + markedSource).Spans, lines);

    /// <summary>The lines, each "id severity: message", with the span of its turn before the colon.</summary>
    public static string OnSpans(List<TextSpan> spans, string[] lines) => string.Join("\n", lines.Select((line, index) =>
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{line[..colon]} [{spans[index].Start}..{spans[index].End}]{line[colon..]}");
    }));

    /// <summary>The model of the type named <paramref name="fullName"/>, for example <c>global::Shop.E</c>. It fails the test if there is none.</summary>
    public static TypeModel Model(string source, string fullName, params MetadataReference[] references) =>
        Results(Header + source, references).Select(result => result.Type).OfType<TypeModel>().Single(type => type.FullName == fullName);

    /// <summary>The model of a type in a source with its own namespace declarations.</summary>
    public static TypeModel ModelOfWholeFile(string source, string fullName) =>
        Results(source, []).Select(result => result.Type).OfType<TypeModel>().Single(type => type.FullName == fullName);

    /// <summary>The build results of all types in a source, entities first.</summary>
    public static IReadOnlyList<BuildResult> Results(string source, MetadataReference[] references, LanguageVersion language = LanguageVersion.Latest)
    {
        var compilation = GeneratorRun.CreateCompilation(source, language).AddReferences(references);
        var run = GeneratorRun.CreateDriver(new DynaSharpGenerator(), language, trackSteps: true).RunGenerators(compilation).GetRunResult().Results[0];
        return [.. new[] { TrackingNames.Entities, TrackingNames.Documents }
            .SelectMany(name => run.TrackedSteps.TryGetValue(name, out var steps) ? steps : [])
            .SelectMany(step => step.Outputs)
            .Select(output => (BuildResult)output.Value)];
    }

    /// <summary>Compiles a small library, for a base type that lives in another assembly.</summary>
    public static MetadataReference Library(string source)
    {
        using var image = new MemoryStream();
        LibraryCompilation(source).Emit(image);
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    /// <summary>Compiles a small library as an IDE references another project: the base type stays source.</summary>
    public static MetadataReference ProjectReference(string source) => LibraryCompilation(source).ToMetadataReference();

    public static string Describe(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.SourceSpan;
        return $"{diagnostic.Id} {diagnostic.Severity} [{span.Start}..{span.End}]: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public static (string Text, List<TextSpan> Spans) Strip(string marked)
    {
        var text = new StringBuilder();
        var spans = new List<TextSpan>();
        var position = 0;
        while (marked.IndexOf(Open, position, StringComparison.Ordinal) is var open and >= 0)
        {
            var close = marked.IndexOf(Close, open, StringComparison.Ordinal);
            text.Append(marked, position, open - position);
            var start = text.Length;
            text.Append(marked, open + Open.Length, close - open - Open.Length);
            spans.Add(new TextSpan(start, text.Length - start));
            position = close + Close.Length;
        }

        text.Append(marked, position, marked.Length - position);
        return (text.ToString(), spans);
    }

    private static CSharpCompilation LibraryCompilation(string source) => GeneratorRun.CreateCompilation(string.Empty, LanguageVersion.Latest)
        .WithAssemblyName("BaseLibrary")
        .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, GeneratorRun.ParseOptions(LanguageVersion.Latest), "Base.cs"));
}
