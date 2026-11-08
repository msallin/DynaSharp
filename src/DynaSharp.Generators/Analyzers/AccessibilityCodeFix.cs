using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA016: <c>private enum Level</c> becomes <c>internal enum Level</c>, or <c>public</c> for a key of a public entity, with each containing type or type argument of the property's type
/// that is less visible. Offered only where every such type is declared in a file of the solution that can be edited. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AccessibilityCodeFix))]
[Shared]
internal sealed class AccessibilityCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every property, so a fix-all raises each type.</summary>
    internal const string Key = "DYNA016.Raise";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.InaccessibleType.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        foreach (var diagnostic in context.Diagnostics)
        {
            var declaration = SourceEdits.NodeAt(root, diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>()!;
            var type = model.GetDeclaredSymbol(declaration, token)!;
            if (Plan(document.Project.Solution, type, diagnostic.Properties[DiagnosticInfo.FixProperty]!, token) is { } plan)
            {
                var title = "Make " + string.Join(" and ", plan.Types.Select(static found => found.Name)) + " " + plan.Keyword;
                context.RegisterCodeFix(CodeAction.Create(title, edit => SourceEdits.ApplyAsync(document.Project.Solution, plan.Changes, edit), Key), diagnostic);
            }
        }
    }

    // The keyword, the types it goes on, and the changes, or null where a type is not in a file that can be edited.
    private static (string Keyword, List<INamedTypeSymbol> Types, List<(DocumentId, TextChange)> Changes)? Plan(Solution solution, INamedTypeSymbol type, string fix, CancellationToken token)
    {
        var parts = fix.Split(' ');
        var keyword = parts[0];
        var property = SourceEdits.PropertyOf(type, parts[1]);
        var types = Types.Below(property.Type, keyword == "public" ? Accessibility.Public : Accessibility.Internal).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
        var changes = new List<(DocumentId, TextChange)>();
        foreach (var below in types)
        {
            var declarations = below.DeclaringSyntaxReferences.Select(reference => SourceEdits.Editable(solution, reference, token)).ToList();
            if (declarations.DefaultIfEmpty().Any(static declaration => declaration is null))
            {
                return null;
            }

            changes.AddRange(Raise([.. declarations.Select(static declaration => declaration!.Value)], keyword, token));
        }

        return (keyword, types, changes);
    }

    // Every part that names an accessibility gets the keyword in its place; where none does, the first part gets it in front of its modifiers.
    private static IEnumerable<(DocumentId Document, TextChange Change)> Raise(List<(Document Document, SyntaxNode Node)> declarations, string keyword, CancellationToken token)
    {
        var named = declarations.Where(static declaration => SourceEdits.NamesAccessibility(Modifiers(declaration.Node))).ToList();
        if (named.Count == 0)
        {
            var (document, node) = declarations[0];
            var modifiers = Modifiers(node);
            var start = modifiers.Count == 0 ? Keyword(node).SpanStart : modifiers[0].SpanStart;
            return [(document.Id, new TextChange(new TextSpan(start, 0), keyword + " "))];
        }

        return named.SelectMany(declaration => SourceEdits.ReplaceAccessibility(declaration.Node.SyntaxTree.GetText(token), Modifiers(declaration.Node), keyword).Select(change => (declaration.Document.Id, change)));
    }

    private static SyntaxTokenList Modifiers(SyntaxNode node) => ((MemberDeclarationSyntax)node).Modifiers;

    // The keyword that says what the declaration is: class, struct, record, interface, enum or delegate.
    private static SyntaxToken Keyword(SyntaxNode node) => node switch
    {
        TypeDeclarationSyntax type => type.Keyword,
        EnumDeclarationSyntax enumeration => enumeration.EnumKeyword,
        _ => ((DelegateDeclarationSyntax)node).DelegateKeyword,
    };
}
