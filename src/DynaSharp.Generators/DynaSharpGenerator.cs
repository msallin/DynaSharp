using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// Generates the mapping code of DynaSharp entities and documents. Models and diagnostics take separate paths: emitters see models only,
/// and only the diagnostic path combines with the compilation, because that node reruns on every edit. Assembly attributes need no node of their own to reach a model: the transform of
/// <c>ForAttributeWithMetadataName</c> runs on every compilation change, in any file, and an equal model stops there.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DynaSharpGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = Models(context, WellKnownNames.EntityAttribute, MappedKind.Entity, TrackingNames.Entities);
        var documents = Models(context, WellKnownNames.DocumentAttribute, MappedKind.Document, TrackingNames.Documents);

        // What is wrong with an assembly attribute belongs to no type, so it is looked at once, whether a type uses it or not. No problem is no item, so a clean compilation has no output here.
        var defaultsProblems = context.CompilationProvider
            .SelectMany(static (compilation, _) => ProjectDefaults.Problems(compilation.Assembly))
            .WithTrackingName(TrackingNames.Diagnostics);
        context.RegisterSourceOutput(
            defaultsProblems.Combine(context.CompilationProvider),
            static (output, input) => DiagnosticReporter.Report(output, input.Left, input.Right));

        // One value that nearly never changes, so that the emitters still rerun per type.
        var clashes = entities.Collect().Combine(documents.Collect()).Select(static (types, _) => HintNames.FindClashes(types.Left, types.Right));
        foreach (var models in new[] { entities, documents })
        {
            context.RegisterSourceOutput(models.Combine(clashes), static (output, input) => EntityEmitter.Emit(output, input.Left!, HintNames.Of(input.Left!, input.Right)));
        }

        // IncludingAll() of the assembly: all entities at once, beside a type of the assembly that takes its name, which changes only with an edit of such a type. Both are values, so an edit that changes no entity reruns nothing.
        var registry = entities
            .Select(static (type, _) => RegistryBuilder.Entry(type!))
            .Collect()
            .Combine(context.CompilationProvider.Select(static (compilation, _) => RegistryBuilder.Clash(compilation)))
            .Select(static (input, _) => RegistryBuilder.Build(input.Left, input.Right))
            .WithTrackingName(TrackingNames.Registry);
        context.RegisterSourceOutput(registry, static (output, built) => EntitiesEmitter.Emit(output, built.Class));
        var registryProblems = registry.SelectMany(static (built, _) => built.Diagnostics.Count > 0 ? [built.Diagnostics] : ImmutableArray<EquatableArray<DiagnosticInfo>>.Empty);
        context.RegisterSourceOutput(
            registryProblems.Combine(context.CompilationProvider),
            static (output, input) => DiagnosticReporter.Report(output, input.Left, input.Right));
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
