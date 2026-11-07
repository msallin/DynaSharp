using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>The table an entity declares, through <c>[DynamoDbEntity(Table = ...)]</c> or the default of its assembly, whose name follows the rules of <see cref="ResourceNames"/>.</summary>
internal static class TableNames
{
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
}
