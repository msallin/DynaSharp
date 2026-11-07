using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Chooses the constructor reads use and binds its parameters to properties.</summary>
internal static class ConstructorBinder
{
    /// <summary>Chooses and binds a constructor.</summary>
    /// <returns>The constructor, or null when none can be chosen, which has been reported.</returns>
    public static IMethodSymbol? Bind(INamedTypeSymbol type, List<PropertyFacts> properties, DiagnosticBag bag, Spot typeSpot)
    {
        var constructor = Choose(type);
        if (constructor is null)
        {
            bag.Report(Descriptors.NoUsableConstructor, typeSpot, type.Name);
            return null;
        }

        foreach (var parameter in constructor.Parameters)
        {
            // The generated read passes a value, which `in` takes and these do not.
            if (parameter.RefKind is RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter)
            {
                bag.Report(Descriptors.ByReferenceParameter, Spot.Of(parameter, typeSpot), parameter.Name, Modifier(parameter.RefKind));
                continue;
            }

            var property = FindProperty(parameter, properties);
            if (property is null)
            {
                bag.Report(Descriptors.UnboundParameter, Spot.Of(parameter, typeSpot), parameter.Name);
            }
            else
            {
                property.Parameter = parameter;
            }
        }

        return constructor;
    }

    private static string Modifier(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref",
        RefKind.Out => "out",
        _ => "ref readonly",
    };

    private static IMethodSymbol? Choose(INamedTypeSymbol type)
    {
        var candidates = type.InstanceConstructors.Where(constructor => !IsCopyConstructor(type, constructor)).ToList();
        var marked = candidates.Where(static constructor => AttributeReader.Find(constructor, WellKnownNames.ConstructorAttribute) is not null).ToList();
        if (marked.Count > 0)
        {
            return marked.Count == 1 ? marked[0] : null;
        }

        // A struct always has an implicit parameterless constructor, which must not win over the one the author wrote.
        var declared = candidates.Where(static constructor => constructor.Parameters.Length > 0 || !constructor.IsImplicitlyDeclared).ToList();
        if (type.IsValueType && declared.Count == 0)
        {
            return candidates[0];
        }

        var parameterless = candidates.Find(static constructor => constructor.Parameters.Length == 0 && !constructor.ContainingType.IsValueType)
            ?? declared.Find(static constructor => constructor.Parameters.Length == 0);
        if (parameterless is not null)
        {
            return parameterless;
        }

        return declared.Count == 1 ? declared[0] : null;
    }

    private static bool IsCopyConstructor(INamedTypeSymbol type, IMethodSymbol constructor) =>
        type.IsRecord && constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type);

    private static PropertyFacts? FindProperty(IParameterSymbol parameter, List<PropertyFacts> properties)
    {
        var bindable = properties.Where(property => !property.IsIgnored && SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type)).ToList();
        return bindable.Find(property => property.Name == parameter.Name)
            ?? bindable.Find(property => string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
    }
}
