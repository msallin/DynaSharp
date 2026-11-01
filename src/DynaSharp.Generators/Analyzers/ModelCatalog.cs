using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>The models of the mapped types one compilation uses, each built once, so that an analyzer knows what the generator knows, also of a type from metadata.</summary>
/// <param name="compilation">The compilation.</param>
internal sealed class ModelCatalog(Compilation compilation)
{
    private readonly ConcurrentDictionary<INamedTypeSymbol, TypeModel?> _models = new(SymbolEqualityComparer.Default);

    /// <summary>The model of a type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The model. Item, a type without an attribute and a type with errors have none, so nothing can be said about them.</returns>
    public TypeModel? ModelOf(INamedTypeSymbol type) => _models.GetOrAdd(type, Read);

    private TypeModel? Read(INamedTypeSymbol type)
    {
        MappedKind kind;
        if (AttributeReader.Find(type, WellKnownNames.EntityAttribute) is not null)
        {
            kind = MappedKind.Entity;
        }
        else if (AttributeReader.Find(type, WellKnownNames.DocumentAttribute) is not null)
        {
            kind = MappedKind.Document;
        }
        else
        {
            return null;
        }

        return ModelBuilder.ModelOf(type, kind, compilation);
    }
}
