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

    public static IEnumerable<AttributeData> FindAll(ISymbol symbol, string name) =>
        symbol.GetAttributes().Where(attribute => IsNamed(attribute.AttributeClass, name));

    /// <summary>The string at a constructor position, or null.</summary>
    public static string? Text(AttributeData attribute, int index) =>
        index < attribute.ConstructorArguments.Length ? attribute.ConstructorArguments[index].Value as string : null;

    /// <summary>The string of a named argument, or null.</summary>
    public static string? Named(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value as string;

    /// <summary>The flag of a named argument, false when it is not set.</summary>
    public static bool Flag(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value is true;
}
