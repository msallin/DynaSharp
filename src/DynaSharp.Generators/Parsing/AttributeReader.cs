using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>
/// Reads attribute data. Arguments that do not bind are compile errors, so every read tolerates them and returns null rather than
/// throwing: an exception in a generator becomes CS8785 and removes all output.
/// </summary>
internal static class AttributeReader
{
    public static bool IsNamed(INamedTypeSymbol? type, string name) => type?.ToDisplayString() == name;

    public static AttributeData? Find(ISymbol symbol, string name) =>
        symbol.GetAttributes().FirstOrDefault(attribute => IsNamed(attribute.AttributeClass, name));

    /// <summary>
    /// The attribute on a property, or on the property it overrides when the override has none of that kind: the mapping attributes are not inherited, and an override that left them out would
    /// otherwise map in another way than the property it overrides. The nearest one wins, so an override that has its own says more than its base.
    /// </summary>
    public static AttributeData? FindOnProperty(IPropertySymbol property, string name)
    {
        for (var current = property; current is not null; current = current.OverriddenProperty)
        {
            if (Find(current, name) is { } attribute)
            {
                return attribute;
            }
        }

        return null;
    }

    public static IEnumerable<AttributeData> FindAll(ISymbol symbol, string name) =>
        symbol.GetAttributes().Where(attribute => IsNamed(attribute.AttributeClass, name));

    /// <summary>The string at a constructor position, or null.</summary>
    public static string? Text(AttributeData attribute, int index) =>
        index < attribute.ConstructorArguments.Length ? attribute.ConstructorArguments[index].Value as string : null;

    /// <summary>The strings of a constructor that takes <c>params string[]</c> and nothing else, none when the call passes null or does not bind, and an empty one for a null among them.</summary>
    public static IEnumerable<string> Texts(AttributeData attribute) =>
        attribute.ConstructorArguments
            .Where(static argument => !argument.IsNull)
            .SelectMany(static argument => argument.Values)
            .Select(static value => value.Value as string ?? string.Empty);

    /// <summary>The string of a named argument, or null.</summary>
    public static string? Named(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value as string;

    /// <summary>The flag of an attribute that says yes by being there and no with <c>(false)</c>.</summary>
    public static bool IsEnabled(AttributeData attribute) => attribute.ConstructorArguments.FirstOrDefault().Value is not false;

    /// <summary>The number of a named argument, which is how an enum arrives, 0 when it is not set.</summary>
    public static int Number(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value is int number ? number : 0;

    /// <summary>The flag of a named argument, false when it is not set.</summary>
    public static bool Flag(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value is true;
}
