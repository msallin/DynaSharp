using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Finds the properties a type could map: its own and those of its base types, base first, each name once.</summary>
internal static class PropertyCollector
{
    public static List<PropertyFacts> Collect(INamedTypeSymbol type, Compilation compilation, Spot typeSpot)
    {
        var seen = new HashSet<string>();
        var levels = new Stack<List<PropertyFacts>>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            var level = new List<PropertyFacts>();
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                // The most derived declaration wins: an override or a `new` property hides the base one. An override still takes the mapping attributes of the one it overrides, see PropertyFacts.
                if (seen.Add(property.Name) && IsCandidate(property, type, compilation))
                {
                    var isSettable = property.SetMethod is { } setter && compilation.IsSymbolAccessibleWithin(setter, type);
                    level.Add(new PropertyFacts(property, isSettable, Spot.Of(property, typeSpot)));
                }
            }

            levels.Push(level);
        }

        var properties = levels.SelectMany(static level => level).ToList();
        var parameterNames = new HashSet<string>();
        foreach (var property in properties)
        {
            var name = Names.Parameter(property.Name);
            while (!parameterNames.Add(name))
            {
                name += "_";
            }

            property.ParameterName = name;
        }

        return properties;
    }

    private static bool IsCandidate(IPropertySymbol property, INamedTypeSymbol type, Compilation compilation)
    {
        if (property.IsStatic || property.IsIndexer || property.GetMethod is not { } getter || !compilation.IsSymbolAccessibleWithin(getter, type))
        {
            return false;
        }

        return property.DeclaredAccessibility == Accessibility.Public
            || AttributeReader.FindOnProperty(property, WellKnownNames.AttributeAttribute) is not null
            || AttributeReader.FindOnProperty(property, WellKnownNames.PartitionKeyAttribute) is not null
            || AttributeReader.FindOnProperty(property, WellKnownNames.SortKeyAttribute) is not null;
    }
}
