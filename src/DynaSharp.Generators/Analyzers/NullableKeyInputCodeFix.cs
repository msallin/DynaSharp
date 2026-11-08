using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA107, the two ways its message names: <c>Sparse = true</c> on the index, and the property that is null <c>required</c> and not nullable, its initializer dropped (a
/// parameter of a record loses its <c>?</c> alone). The second is offered for a property in source with a setter, written <c>T?</c>; the first for an index.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NullableKeyInputCodeFix))]
[Shared]
internal sealed class NullableKeyInputCodeFix : TextEditCodeFix
{
    /// <summary>The equivalence key of the fix that makes the index sparse.</summary>
    internal const string SparseKey = "DYNA107.Sparse";

    /// <summary>The equivalence key of the fix that makes the property required and not nullable.</summary>
    internal const string RequiredKey = "DYNA107.Required";

    private const string SparseArgument = "Sparse";

    private static readonly string[] IndexAttributes = [WellKnownNames.GlobalIndexKeyAttribute, WellKnownNames.LocalIndexKeyAttribute];

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NullableInput.Id];

    /// <inheritdoc/>
    protected override async Task<IEnumerable<FixOption>> FixesAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var span = diagnostic.Location.SourceSpan;
        var name = FixOf(diagnostic)![0];
        var options = new List<FixOption>();

        // A key attribute of a compiled base leaves the diagnostic on the type.
        var attribute = SourceEdits.AttributeAt(root, span);
        if (attribute is not null && IndexAttributes.Any(index => AttributeReader.IsNamed(model.GetTypeInfo(attribute, token).Type as INamedTypeSymbol, index)))
        {
            options.Add(new FixOption("Set Sparse = true", SparseKey, [(document.Id, SourceEdits.SetNamedArgument(attribute, SparseArgument, "true"))]));
        }

        if (await PropertyAsync(document, span, name, token).ConfigureAwait(false) is { } found && Required(found.Node) is { } edit)
        {
            options.Add(new FixOption(edit.Title.Replace("{0}", name), RequiredKey, [.. edit.Changes.Select(change => (found.Document.Id, change))]));
        }

        return options;
    }

    // string? Email { get; set; } = null; gives required string Email { get; set; }. A parameter of a record keeps no default but null, so it loses its ? alone.
    private static (string Title, TextChange[] Changes)? Required(SyntaxNode declaration)
    {
        if (declaration is ParameterSyntax { Type: NullableTypeSyntax parameterType, Default: null })
        {
            return ("Make {0} non-nullable", [Remove(parameterType.QuestionToken.Span)]);
        }

        if (declaration is not PropertyDeclarationSyntax { Type: NullableTypeSyntax type, AccessorList: { } accessors } property
            || !accessors.Accessors.Any(static accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration)))
        {
            return null;
        }

        var changes = new List<TextChange>();
        var isRequired = property.Modifiers.Any(SyntaxKind.RequiredKeyword);
        if (!isRequired)
        {
            changes.Add(new TextChange(new TextSpan(type.SpanStart, 0), "required "));
        }

        changes.Add(Remove(type.QuestionToken.Span));
        if (property.Initializer is not null)
        {
            changes.Add(Remove(TextSpan.FromBounds(accessors.Span.End, property.SemicolonToken.Span.End)));
        }

        return (isRequired ? "Make {0} non-nullable" : "Make {0} required and non-nullable", [.. changes]);
    }

    private static TextChange Remove(TextSpan span) => new(span, string.Empty);
}
