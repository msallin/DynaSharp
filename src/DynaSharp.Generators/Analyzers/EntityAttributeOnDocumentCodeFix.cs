using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA012, the two ways its message names: the attribute only an entity can have goes, or the <c>[DynamoDbDocument]</c> of the type becomes <c>[DynamoDbEntity]</c>, which the
/// diagnostic finds by the id of the type in its property <c>Fix</c>. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityAttributeOnDocumentCodeFix))]
[Shared]
internal sealed class EntityAttributeOnDocumentCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that removes the attribute.</summary>
    internal const string RemoveKey = "DYNA012.Remove";

    /// <summary>The equivalence key of the fix that makes the document an entity.</summary>
    internal const string EntityKey = "DYNA012.Entity";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NotValidOnDocument.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var compilation = (await document.Project.GetCompilationAsync(token).ConfigureAwait(false))!;
        var solution = document.Project.Solution;
        foreach (var diagnostic in context.Diagnostics)
        {
            // An attribute in a library stands on the type.
            var attribute = SourceEdits.AttributeAt(root, diagnostic.Location.SourceSpan);
            if (SourceEdits.RemoveAttribute(solution, attribute?.GetReference(), token) is { } removal)
            {
                var name = SourceEdits.ShortName(compilation.GetSemanticModel(root.SyntaxTree).GetSymbolInfo(attribute!, token).Symbol!.ContainingType);
                context.RegisterCodeFix(CodeAction.Create("Remove [" + name + "]", edit => SourceEdits.ApplyAsync(solution, [removal], edit), RemoveKey), diagnostic);
            }

            var type = (INamedTypeSymbol)DocumentationCommentId.GetFirstSymbolForDeclarationId(diagnostic.Properties[DiagnosticInfo.FixProperty]!, compilation)!;
            if (SourceEdits.Editable(solution, AttributeReader.Find(type, WellKnownNames.DocumentAttribute)!.ApplicationSyntaxReference, token) is { } place)
            {
                var marker = (AttributeSyntax)place.Node;
                var entity = SourceEdits.AttributeName(compilation.GetSemanticModel(marker.SyntaxTree), marker.SpanStart, WellKnownNames.EntityAttribute);
                var change = (place.Document.Id, new TextChange(marker.Name.Span, entity));
                context.RegisterCodeFix(CodeAction.Create("Make " + type.Name + " an entity", edit => SourceEdits.ApplyAsync(solution, [change], edit), EntityKey), diagnostic);
            }
        }
    }
}
