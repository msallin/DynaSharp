using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>A place in a source file, as a value. Metadata has no such place, so every lookup takes a fallback in source.</summary>
/// <param name="FilePath">The path of the tree.</param>
/// <param name="Span">The span in that tree.</param>
internal readonly record struct Spot(string FilePath, TextSpan Span)
{
    public static Spot Of(Location location) => new(location.SourceTree!.FilePath, location.SourceSpan);

    // A symbol from a referenced assembly has only metadata locations.
    public static Spot Of(ISymbol symbol, Spot fallback) =>
        symbol.Locations.Where(static location => location.IsInSource).Select(static location => Of(location)).DefaultIfEmpty(fallback).First();

    public static Spot Of(AttributeData attribute, Spot fallback) =>
        attribute.ApplicationSyntaxReference?.GetSyntax() is { } syntax ? Of(syntax.GetLocation()) : fallback;

    /// <summary>The positional argument at <paramref name="index"/>.</summary>
    public static Spot Argument(AttributeData attribute, int index, Spot fallback) => Arguments(attribute)
        .Skip(index)
        .Select(static argument => Of(argument.GetLocation()))
        .DefaultIfEmpty(Of(attribute, fallback))
        .First();

    /// <summary>The value of the named argument <paramref name="name"/>.</summary>
    public static Spot Argument(AttributeData attribute, string name, Spot fallback) => Arguments(attribute)
        .Where(argument => argument.NameEquals?.Name.Identifier.ValueText == name)
        .Select(static argument => Of(argument.Expression.GetLocation()))
        .DefaultIfEmpty(Of(attribute, fallback))
        .First();

    // An attribute from metadata has no reference, and one written without parentheses has no list.
    private static SeparatedSyntaxList<AttributeArgumentSyntax> Arguments(AttributeData attribute)
    {
        var syntax = (AttributeSyntax?)attribute.ApplicationSyntaxReference?.GetSyntax();
        return syntax?.ArgumentList?.Arguments ?? default;
    }
}
