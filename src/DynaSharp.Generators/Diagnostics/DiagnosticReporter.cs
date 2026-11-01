using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>
/// Turns diagnostic values back into diagnostics. This is the only code after the transform that touches the compilation, and it runs
/// only for types that have diagnostics.
/// </summary>
internal static class DiagnosticReporter
{
    public static void Report(SourceProductionContext output, EquatableArray<DiagnosticInfo> diagnostics, Compilation compilation)
    {
        foreach (var info in diagnostics)
        {
            // Only a location in a real tree reaches #pragma and .editorconfig. There is none for a base type in another project of an IDE
            // workspace, which is source without being in this compilation.
            var tree = compilation.SyntaxTrees.FirstOrDefault(candidate => candidate.FilePath == info.FilePath);
            var location = tree is null ? Location.None : Location.Create(tree, info.Span);
            output.ReportDiagnostic(Diagnostic.Create(Descriptors.ById[info.Id], location, [.. info.Arguments]));
        }
    }
}
