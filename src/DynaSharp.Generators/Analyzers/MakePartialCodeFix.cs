using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA001: <c>partial</c> on the type and on each type that contains it and lacks it. A type that is not partial has one declaration, so the one around the diagnostic is all there is to
/// edit. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakePartialCodeFix))]
[Shared]
internal sealed class MakePartialCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every type, so that a fix-all makes each partial.</summary>
    internal const string Key = "DYNA001.Partial";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NotPartial.Id];

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
            // The generator reports on the identifier of the type. The outermost type comes first, as in its name.
            var type = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent!.FirstAncestorOrSelf<TypeDeclarationSyntax>()!;
            var lacking = type.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Where(static declaration => !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)).Reverse().ToList();
            var title = "Add partial to " + string.Join(", ", lacking.Select(static declaration => declaration.Identifier.ValueText));
            context.RegisterCodeFix(CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(root.ReplaceNodes(lacking, static (_, rewritten) => WithPartial(rewritten)))), Key), diagnostic);
        }
    }

    // Partial goes right before class, struct, record or interface, the one place C# allows, and takes the trivia that led the keyword: the indent, or the doc comment of a type without modifiers.
    private static TypeDeclarationSyntax WithPartial(TypeDeclarationSyntax declaration)
    {
        var keyword = declaration.Keyword;
        var partial = SyntaxFactory.Token(keyword.LeadingTrivia, SyntaxKind.PartialKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space));
        return declaration.WithKeyword(keyword.WithLeadingTrivia(default(SyntaxTriviaList))).WithModifiers(declaration.Modifiers.Add(partial));
    }
}
