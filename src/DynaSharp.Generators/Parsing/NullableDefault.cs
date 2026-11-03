using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// Finds a nullable property that starts with a value, which a null cannot survive: a write leaves null out of the item, and a read of an item without the attribute keeps
/// what the initializer of the property or the default of its constructor parameter gives. Only syntax counts, as in <see cref="EmptyDefault"/>, so a value that a
/// constructor body assigns is left alone.
/// </summary>
internal static class NullableDefault
{
    /// <summary>What a read keeps where null was written, and the fix, or null when null reads back as null.</summary>
    /// <param name="property">The property.</param>
    /// <param name="type">The type being analysed.</param>
    /// <param name="compilation">The compilation of the type.</param>
    /// <param name="typeSpot">Where the type is, for a property of a base type that another project declares.</param>
    /// <returns>The arguments of <see cref="Descriptors.NullReadsBackAsInitialValue"/> after the name, and where to report, or null.</returns>
    public static (string Kept, string Fix, Spot Spot)? Of(PropertyFacts property, INamedTypeSymbol type, Compilation compilation, Spot typeSpot)
    {
        // A computed property is written and never read.
        if (!property.IsStored || property.IsComputed || !Types.Unwrap(property.Type).IsNullable)
        {
            return null;
        }

        if (property.Parameter is { } parameter)
        {
            return parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: not null }
                ? ($"the default value of parameter '{parameter.Name}'", "Make the default value null, or make the property non-nullable", Spot.Of(parameter, property.Spot))
                : null;
        }

        // A mapped base type reports its own property, so a derived type does not report it a second time.
        var owner = property.Symbol.ContainingType;
        if (!SymbolEqualityComparer.Default.Equals(owner, type) && (AttributeReader.Find(owner, WellKnownNames.EntityAttribute) ?? AttributeReader.Find(owner, WellKnownNames.DocumentAttribute)) is not null)
        {
            return null;
        }

        // A base type in another project of the solution shows its syntax but has no place in this compilation, and one from a package shows none.
        return StartsWithAValue(property.Symbol) is { } declaration
            ? ("the value of its initializer", "Remove the initializer, or make the property non-nullable", compilation.ContainsSyntaxTree(declaration.SyntaxTree) ? property.Spot : typeSpot)
            : null;
    }

    private static PropertyDeclarationSyntax? StartsWithAValue(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(declaration => declaration.Initializer is { } initializer && !IsNull(initializer.Value, property.Type));

    // default(int) on an int? is 0, which is a value, and default(int?) is null.
    private static bool IsNull(ExpressionSyntax value, ITypeSymbol type) => value switch
    {
        PostfixUnaryExpressionSyntax suppressed => IsNull(suppressed.Operand, type),
        LiteralExpressionSyntax literal => literal.Kind() is SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression,
        DefaultExpressionSyntax expression => type.IsReferenceType || expression.Type is NullableTypeSyntax,
        _ => false,
    };
}
