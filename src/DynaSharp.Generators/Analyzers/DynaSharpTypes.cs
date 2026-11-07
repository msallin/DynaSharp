using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Tells what DynaSharp declares from what a consumer declares, by the namespace, so a new command needs no list.</summary>
internal static class DynaSharpTypes
{
    private const string RootNamespace = "DynaSharp";

    private const string MixedType = "Mixed";

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

    /// <summary>
    /// The type that the table parameter of a method of the library holds: <c>Order</c> for <c>Update(chain, Table&lt;Order&gt; table, key)</c>, and <c>Item</c> for a step that takes the values
    /// of a key, which only a table without a schema takes.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The type, or null for a method without a parameter named <c>table</c>.</returns>
    public static ITypeSymbol? TableTypeOf(IMethodSymbol method)
    {
        var table = method.Parameters.FirstOrDefault(static parameter => parameter.Name == "table");
        return table is null ? null : ((INamedTypeSymbol)table.Type).TypeArguments[0];
    }

    /// <summary>The types a command reads, from its type arguments: those of a <c>Mixed&lt;...&gt;</c> of the library stand for the mix, as the item of a mixed read.</summary>
    /// <param name="command">The type of the command.</param>
    /// <returns><c>Customer, Order</c> for <c>MixedQueryCommand&lt;Mixed&lt;Customer, Order&gt;&gt;</c>, <c>Order</c> for <c>QueryCommand&lt;Order&gt;</c>.</returns>
    public static IEnumerable<ITypeSymbol> TypesReadBy(INamedTypeSymbol command) =>
        command.TypeArguments.SelectMany(static argument => argument is INamedTypeSymbol { Name: MixedType, IsGenericType: true } mixed && IsDeclaredByDynaSharp(mixed) ? mixed.TypeArguments : [argument]);

    /// <summary>Whether a value is a ref struct of the library: a command, a step, a handler, a reader or a writer.</summary>
    /// <param name="type">The type of the value, if it has one.</param>
    /// <returns>Whether the value has the limits of a DynaSharp ref struct.</returns>
    public static bool IsRefStruct(ITypeSymbol? type) => type is { IsRefLikeType: true } && IsDeclaredByDynaSharp(type);

    /// <summary>
    /// Whether a value is a command or a step of a transaction: every call on one uses it up and gives the next one back. Those are the ref structs of the root namespace; handlers, readers and writers
    /// live below it. In a generic helper it is a type parameter that can be a ref struct and has a fluent interface of the library, such as <c>IConditional&lt;TSelf&gt;</c>, over itself. Those are in the root
    /// namespace too. <c>IDynamoDbDocument&lt;TSelf&gt;</c> is over itself as well, and it is in <c>DynaSharp.Mapping</c> and says what a value is, not that a call uses it up.
    /// </summary>
    /// <param name="type">The type of the value, if it has one.</param>
    /// <returns>Whether the value is a handle to a command.</returns>
    public static bool IsCommand(ITypeSymbol? type) => type is ITypeParameterSymbol parameter
        ? parameter.AllowsRefLikeType && parameter.ConstraintTypes.Any(constraint => IsFluentOver(constraint, parameter))
        : type is { IsRefLikeType: true } && IsInRootNamespace(type);

    private static bool IsFluentOver(ITypeSymbol constraint, ITypeParameterSymbol parameter) =>
        constraint is INamedTypeSymbol { TypeArguments.Length: 1 } generic && SymbolEqualityComparer.Default.Equals(generic.TypeArguments[0], parameter) && IsInRootNamespace(constraint);

    private static bool IsInRootNamespace(ITypeSymbol type) => type.ContainingNamespace is { Name: RootNamespace, ContainingNamespace.IsGlobalNamespace: true };
}
