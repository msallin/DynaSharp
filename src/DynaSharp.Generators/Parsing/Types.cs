using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Questions about type symbols.</summary>
internal static class Types
{
    /// <summary>Removes nullability: <c>int?</c> gives <c>int</c>, <c>string?</c> gives <c>string</c>.</summary>
    /// <returns>The type without nullability, and whether it had any.</returns>
    public static (ITypeSymbol Inner, bool IsNullable) Unwrap(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? (nullable.TypeArguments[0], true)
            : (type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), type.NullableAnnotation == NullableAnnotation.Annotated);

    /// <summary>Whether a type or a part of it did not resolve. The compiler reports that, and nothing here should add to it.</summary>
    public static bool HasError(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => HasError(array.ElementType),
        INamedTypeSymbol named => named.TypeKind == TypeKind.Error || named.TypeArguments.Any(HasError),
        _ => false,
    };

    /// <summary>The accessibility the type really has, through its containing types and type arguments. <c>file</c> types count as private.</summary>
    public static Accessibility Effective(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => Effective(array.ElementType),
        INamedTypeSymbol { IsFileLocal: true } => Accessibility.Private,
        INamedTypeSymbol named => EffectiveOfNamed(named),
        _ => Accessibility.Public,
    };

    /// <summary>Whether generated code, parts of which live outside the type, can name it.</summary>
    public static bool IsAccessible(ITypeSymbol type) => Effective(type) >= Accessibility.Internal;

    private static Accessibility EffectiveOfNamed(INamedTypeSymbol type)
    {
        var result = type.DeclaredAccessibility;
        if (type.ContainingType is { } outer)
        {
            result = Lowest(result, Effective(outer));
        }

        foreach (var argument in type.TypeArguments)
        {
            result = Lowest(result, Effective(argument));
        }

        return result;
    }

    private static Accessibility Lowest(Accessibility left, Accessibility right) => left < right ? left : right;
}
