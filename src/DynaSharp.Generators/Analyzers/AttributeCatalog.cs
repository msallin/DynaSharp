using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>The attributes of the mapped types one compilation uses, each worked out once, for what DYNA213 asks of a name: a path argument, and a bare name in the text of an expression.</summary>
/// <param name="models">The models of the compilation.</param>
internal sealed class AttributeCatalog(ModelCatalog models)
{
    private readonly ConcurrentDictionary<INamedTypeSymbol, HashSet<string>?> _names = new(SymbolEqualityComparer.Default);

    /// <summary>The mapped type of a command that has no attribute of this name.</summary>
    /// <remarks>
    /// Commands are ref structs, and a read of several types takes a name that any of them has. <c>Attr&lt;T, TConverter&gt;</c> also starts with a type argument, but that one is the type of the value,
    /// and it is no ref struct. A step of a transaction has no type argument, so it says nothing.
    /// </remarks>
    /// <param name="command">The type that declares the parameter the name is given to.</param>
    /// <param name="root">The first name of the path.</param>
    /// <returns>The first type argument, or null when a type has the name or one cannot be known.</returns>
    public ITypeSymbol? TypeWithout(INamedTypeSymbol command, string root)
    {
        if (!command.IsRefLikeType)
        {
            return null;
        }

        var types = DynaSharpTypes.TypesReadBy(command).ToList();
        foreach (var argument in types)
        {
            if (argument is not INamedTypeSymbol mapped || NamesOf(mapped) is not { } names || names.Contains(root))
            {
                return null;
            }
        }

        return types.FirstOrDefault();
    }

    /// <summary>The attributes of the types a command reads that a typo of a name may have meant, the closest first, for a name <see cref="TypeWithout"/> found on none.</summary>
    /// <param name="command">The type that declares the parameter the name is given to.</param>
    /// <param name="root">The first name of the path.</param>
    /// <returns>The attributes within <see cref="Spelling.TypoDistance"/>.</returns>
    public List<string> CloseTo(INamedTypeSymbol command, string root) =>
        Spelling.Closest(root, DynaSharpTypes.TypesReadBy(command).SelectMany(type => NamesOf((INamedTypeSymbol)type)!), Spelling.TypoDistance(root));

    private static void AddKey(HashSet<string> names, KeyModel key) => names.Add(key.AttributeName);

    private static HashSet<string> NamesIn(TypeModel model)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in model.Properties)
        {
            if (property.RestoredFromKey is null)
            {
                names.Add(property.AttributeName);
            }
        }

        if (model.Entity is { } entity)
        {
            names.Add(entity.DiscriminatorAttribute);
            AddKey(names, entity.PartitionKey);
            AddKeyIfAny(names, entity.SortKey);
            foreach (var index in entity.Indexes)
            {
                AddKey(names, index.PartitionKey);
                AddKeyIfAny(names, index.SortKey);
            }
        }

        return names;
    }

    private static void AddKeyIfAny(HashSet<string> names, KeyModel? key)
    {
        if (key is not null)
        {
            AddKey(names, key);
        }
    }

    private HashSet<string>? NamesOf(INamedTypeSymbol type) => _names.GetOrAdd(type, Read);

    private HashSet<string>? Read(INamedTypeSymbol type) => models.ModelOf(type) is { } model ? NamesIn(model) : null;
}
