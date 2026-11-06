using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>The table an entity declares, through <c>[DynamoDbEntity(Table = ...)]</c> or the default of its assembly, and DynamoDB's rules for its name.</summary>
internal static class TableNames
{
    public const int MinLength = 3;

    public const int MaxLength = 255;

    /// <summary>The name an entity declares on its own attribute, or null.</summary>
    /// <param name="type">The entity, which has <c>[DynamoDbEntity]</c>.</param>
    /// <returns>The name as written, which may be invalid.</returns>
    public static string? OnType(INamedTypeSymbol type) => AttributeReader.Named(AttributeReader.Find(type, WellKnownNames.EntityAttribute)!, WellKnownNames.TableArgument);

    /// <summary>The name of the table of an entity: its own, then the default of its assembly, the one it was built with, also for a type from metadata.</summary>
    /// <param name="type">The entity, which has <c>[DynamoDbEntity]</c>.</param>
    /// <returns>The name, or null when neither declares one.</returns>
    public static string? Of(INamedTypeSymbol type) => OnType(type) ?? OfAssembly(type.ContainingAssembly);

    /// <summary>The default of an assembly, or null.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The name as written, which may be invalid.</returns>
    public static string? OfAssembly(IAssemblySymbol assembly) =>
        AttributeReader.Find(assembly, WellKnownNames.DefaultsAttribute) is { } defaults ? AttributeReader.Named(defaults, WellKnownNames.TableArgument) : null;

    /// <summary>Says what DynamoDB would reject about a table name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The reason, or null for a valid name.</returns>
    public static string? Problem(string name)
    {
        if (name.Length == 0)
        {
            return "it is empty";
        }

        if (name.Length is < MinLength or > MaxLength)
        {
            return $"it has {name.Length.ToString(CultureInfo.InvariantCulture)} characters, and a table name has 3 to 255";
        }

        foreach (var character in name)
        {
            if (!IsAllowed(character))
            {
                return $"'{character}' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'";
            }
        }

        return null;
    }

    private static bool IsAllowed(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.';
}
