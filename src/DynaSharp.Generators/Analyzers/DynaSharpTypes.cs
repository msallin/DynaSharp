using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Tells what DynaSharp declares from what a consumer declares, by the namespace, so a new command needs no list.</summary>
internal static class DynaSharpTypes
{
    private const string RootNamespace = "DynaSharp";

    /// <summary>Whether a type is in the namespace of the library or below it.</summary>
    /// <param name="type">The type.</param>
    /// <returns>True for <c>DynaSharp.Fluent.Page</c>, false for <c>DynaSharpExtras.Page</c>.</returns>
    public static bool IsDeclaredByDynaSharp(ITypeSymbol type)
    {
        var current = type.ContainingNamespace;
        while (current.ContainingNamespace is { IsGlobalNamespace: false } parent)
        {
            current = parent;
        }

        return current.Name == RootNamespace;
    }

    /// <summary>Whether a value is a ref struct of the library: a command, a step, a handler, a reader or a writer.</summary>
    /// <param name="type">The type of the value, if it has one.</param>
    /// <returns>Whether the value has the limits of a DynaSharp ref struct.</returns>
    public static bool IsRefStruct(ITypeSymbol? type) => type is { IsRefLikeType: true } && IsDeclaredByDynaSharp(type);

    /// <summary>
    /// Whether a value is a command or a step of a transaction: every call on one uses it up and gives the next one back. Those are the ref structs of the root namespace; handlers, readers and writers
    /// live below it. In a generic helper it is a type parameter that can be a ref struct and has a fluent interface of the library, such as <c>IConditional&lt;TSelf&gt;</c>, over itself.
    /// </summary>
    /// <param name="type">The type of the value, if it has one.</param>
    /// <returns>Whether the value is a handle to a command.</returns>
    public static bool IsCommand(ITypeSymbol? type) => type is ITypeParameterSymbol parameter
        ? parameter.AllowsRefLikeType && parameter.ConstraintTypes.Any(constraint => IsFluentOver(constraint, parameter))
        : type is { IsRefLikeType: true, ContainingNamespace: { Name: RootNamespace, ContainingNamespace.IsGlobalNamespace: true } };

    private static bool IsFluentOver(ITypeSymbol constraint, ITypeParameterSymbol parameter) =>
        constraint is INamedTypeSymbol { TypeArguments.Length: 1 } generic && SymbolEqualityComparer.Default.Equals(generic.TypeArguments[0], parameter) && IsDeclaredByDynaSharp(constraint);
}
