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
            var location = info.Type is { } type ? EntityAttributeOf(compilation, type) : tree is null ? Location.None : Location.Create(tree, info.Span);
            output.ReportDiagnostic(Diagnostic.Create(Descriptors.ById[info.Id], location, [.. info.Arguments]));
        }
    }

    /// <summary>Where the <c>[DynamoDbEntity]</c> of a type of the compilation stands.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="metadataName">The metadata name of the type.</param>
    /// <returns>The place, or none when the compilation has no such type or the type no such attribute.</returns>
    internal static Location EntityAttributeOf(Compilation compilation, string metadataName) =>
        compilation.Assembly.GetTypeByMetadataName(metadataName) is { } type && AttributeReader.Find(type, WellKnownNames.EntityAttribute)?.ApplicationSyntaxReference is { } reference
            ? reference.GetSyntax().GetLocation()
            : Location.None;
}
