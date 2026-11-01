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
}
