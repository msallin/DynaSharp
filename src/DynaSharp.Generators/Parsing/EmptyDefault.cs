using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// Finds a property that starts empty and that nothing has to set, such as <c>public string Partition { get; set; } = string.Empty;</c>. Only a default that cannot be a key counts: <c>"main"</c> and
/// <c>Guid.NewGuid()</c> are keys of their own, and an initializer that says nothing to syntax is left alone.
/// </summary>
internal static class EmptyDefault
{
    public static bool IsForgettable(PropertyFacts property, INamedTypeSymbol type) =>
        !property.Symbol.IsRequired
        && !property.IsComputed
        && property.Parameter is null
        && !Types.Unwrap(property.Type).IsNullable
        && StartsEmpty(property.Symbol)
        && !IsNamedInConstructor(property.Name, type);

    // A property of a base type in another assembly shows no syntax, and so nothing to report.
    private static bool StartsEmpty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(declaration => IsEmpty(declaration.Initializer?.Value, property.Type));

    // Only a reference type has a null to start from: default of a value type is what no initializer gives too.
    private static bool IsEmpty(ExpressionSyntax? value, ITypeSymbol type) => value switch
    {
        PostfixUnaryExpressionSyntax suppressed => IsEmpty(suppressed.Operand, type),
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText.Length == 0,
        LiteralExpressionSyntax literal => literal.Kind() is SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression && type.IsReferenceType,
        DefaultExpressionSyntax => type.IsReferenceType,
        MemberAccessExpressionSyntax member => member.ToString() is "string.Empty" or "String.Empty" or "System.String.Empty",
        CollectionExpressionSyntax collection => collection.Elements.Count == 0,
        InvocationExpressionSyntax invocation => invocation.ToString() is "Array.Empty<byte>()" or "System.Array.Empty<byte>()",
        _ => false,
    };

    // Any mention in a constructor counts as setting it: an assignment, this.X and a tuple (X, Y) = (x, y) all name it, and a read of a property there is too rare to report past.
    private static bool IsNamedInConstructor(string name, INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var bodies = current.InstanceConstructors
                .SelectMany(static constructor => constructor.DeclaringSyntaxReferences)
                .Select(static reference => reference.GetSyntax())
                .OfType<ConstructorDeclarationSyntax>();
            if (bodies.Any(body => body.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier => identifier.Identifier.ValueText == name)))
            {
                return true;
            }
        }

        return false;
    }
}
