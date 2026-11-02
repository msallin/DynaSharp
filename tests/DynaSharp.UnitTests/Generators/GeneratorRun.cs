using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>Runs a generator in-process over source text, the way the compiler would, at a chosen C# language version.</summary>
internal static class GeneratorRun
{
    // The platform assemblies of the test host, which include DynaSharp.dll. Roslyn here is the generator floor, so "latest" is C# 14.
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    // Diagnose makes the compiler report CS1591 for public members without docs, as a consumer with GenerateDocumentationFile does.
    public static CSharpParseOptions ParseOptions(LanguageVersion language) => new(language, DocumentationMode.Diagnose);

    public static CSharpCompilation CreateCompilation(string source, LanguageVersion language) =>
        CSharpCompilation.Create(
            "GeneratorInput",
            [CSharpSyntaxTree.ParseText(source, ParseOptions(language))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorDriver CreateDriver(IIncrementalGenerator generator, LanguageVersion language, bool trackSteps = false) =>
        CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            parseOptions: ParseOptions(language),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: trackSteps));

    /// <summary>Runs a generator and compiles nothing, for a test of what it writes. Compiling that is the slow part, and the tests that need it are the ones that say so.</summary>
    public static GeneratorDriverRunResult Generate(string source, IIncrementalGenerator generator, LanguageVersion language) =>
        CreateDriver(generator, language).RunGenerators(CreateCompilation(source, language)).GetRunResult();

    /// <summary>Runs a generator and compiles the result.</summary>
    /// <param name="source">The input.</param>
    /// <param name="generator">The generator.</param>
    /// <param name="language">The language version.</param>
    /// <param name="moreSources">Sources to compile as well, from the text of the generated files.</param>
    public static GeneratorRunOutput Run(string source, IIncrementalGenerator generator, LanguageVersion language, Func<IEnumerable<string>, IEnumerable<string>>? moreSources = null)
    {
        var compilation = CreateCompilation(source, language);
        var driver = CreateDriver(generator, language).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();
        var generated = result.GeneratedTrees.ToHashSet();
        var more = moreSources?.Invoke(generated.Select(tree => tree.ToString())) ?? [];
        output = output.AddSyntaxTrees(more.Select(text => CSharpSyntaxTree.ParseText(text, ParseOptions(language))));

        var problems = result.Diagnostics
            .Concat(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error || (d.Location.SourceTree is { } tree && generated.Contains(tree))))
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString());
        return new GeneratorRunOutput(result, [.. problems]);
    }

    private static ImmutableArray<MetadataReference> LoadReferences() =>
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];
}
