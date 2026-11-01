using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// Generates the mapping code of DynaSharp entities and documents. Models and diagnostics take separate paths: emitters see models only,
/// and only the diagnostic path combines with the compilation, because that node reruns on every edit.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DynaSharpGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = Models(context, WellKnownNames.EntityAttribute, MappedKind.Entity, TrackingNames.Entities);
        var documents = Models(context, WellKnownNames.DocumentAttribute, MappedKind.Document, TrackingNames.Documents);

        // One value that nearly never changes, so that the emitters still rerun per type.
        var clashes = entities.Collect().Combine(documents.Collect()).Select(static (types, _) => HintNames.FindClashes(types.Left, types.Right));
        foreach (var models in new[] { entities, documents })
        {
            context.RegisterSourceOutput(models.Combine(clashes), static (output, input) => EntityEmitter.Emit(output, input.Left!, HintNames.Of(input.Left!, input.Right)));
        }
    }

    private static IncrementalValuesProvider<TypeModel?> Models(IncrementalGeneratorInitializationContext context, string attributeName, MappedKind kind, string trackingName)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                (syntax, token) => ModelBuilder.Build(syntax, kind, token))
            .WithTrackingName(trackingName);

        var diagnostics = results
            .Select(static (result, _) => result.Diagnostics)
            .Where(static found => found.Count > 0)
            .WithTrackingName(TrackingNames.Diagnostics);
        context.RegisterSourceOutput(
            diagnostics.Combine(context.CompilationProvider),
            static (output, input) => DiagnosticReporter.Report(output, input.Left, input.Right));

        return results
            .Select(static (result, _) => result.Type)
            .Where(static type => type is not null)
            .WithTrackingName(TrackingNames.Models);
    }
}
