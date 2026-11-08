using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA117: <c>public string Id { get; set; } = string.Empty;</c> becomes <c>public required string Id { get; set; }</c>. Not offered where <c>required</c> would not compile: a property
/// whose setter is less visible than its type. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequireKeyCodeFix))]
[Shared]
internal sealed class RequireKeyCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every property, so that a fix-all makes each required.</summary>
    internal const string Key = "DYNA117.Required";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.EmptyDefaultOfKey.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = (await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        if (GeneratedFiles.IsGenerated(root.SyntaxTree, context.CancellationToken))
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            // The generator reports on the identifier of a property that has an initializer.
            var property = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent!.FirstAncestorOrSelf<PropertyDeclarationSyntax>()!;
            var required = root.ReplaceNode(property, Required(property));
            if (await CompilesAsync(document.WithSyntaxRoot(required), property.SpanStart, context.CancellationToken).ConfigureAwait(false))
            {
                var title = "Make " + property.Identifier.ValueText + " required, without its initializer";
                context.RegisterCodeFix(CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(required)), Key), diagnostic);
            }
        }
    }

    // Required goes last among the modifiers, as in public required string, since only a public property is mapped. The semicolon's trailing trivia, a comment or the line end, moves to the
    // accessors.
    private static PropertyDeclarationSyntax Required(PropertyDeclarationSyntax property) => property
        .WithInitializer(null)
        .WithSemicolonToken(default)
        .WithAccessorList(property.AccessorList!.WithTrailingTrivia(property.SemicolonToken.TrailingTrivia))
        .WithModifiers(property.Modifiers.Add(SyntaxFactory.Token(default, SyntaxKind.RequiredKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space))));

    // The compiler knows best whether a property can be required: its setter has to be as visible as its type (CS9032).
    private static async Task<bool> CompilesAsync(Document document, int start, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var property = root.FindToken(start).Parent!.FirstAncestorOrSelf<PropertyDeclarationSyntax>()!;
        return !model.GetDiagnostics(property.Span, token).Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
