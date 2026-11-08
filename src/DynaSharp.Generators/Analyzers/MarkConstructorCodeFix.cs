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
/// The fixes of DYNA005, one for each constructor a read could use: <c>[DynamoDbConstructor]</c> on it, <c>[method: DynamoDbConstructor]</c> on the type for a primary constructor, and off every
/// other constructor that has it. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MarkConstructorCodeFix))]
[Shared]
internal sealed class MarkConstructorCodeFix : CodeFixProvider
{
    /// <summary>The start of the equivalence key of each fix, which ends with the position of its constructor among those of the type.</summary>
    internal const string KeyPrefix = "DYNA005.";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NoUsableConstructor.Id];

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var solution = document.Project.Solution;
        var compilation = (await document.Project.GetCompilationAsync(token).ConfigureAwait(false))!;
        foreach (var diagnostic in context.Diagnostics)
        {
            var type = model.GetDeclaredSymbol(SourceEdits.NodeAt(root, diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>()!, token)!;
            var candidates = ConstructorBinder.Candidates(type);

            // Where several have the attribute, the fix is to keep it on one of them.
            var offered = candidates.Count(ConstructorBinder.IsMarked) > 1 ? candidates.Where(ConstructorBinder.IsMarked).ToList() : candidates;
            for (var index = 0; index < offered.Count; index++)
            {
                if (Plan(solution, compilation, offered[index], candidates, token) is { } changes)
                {
                    var title = "Mark " + Signature(offered[index]) + " with [DynamoDbConstructor]";
                    context.RegisterCodeFix(CodeAction.Create(title, edit => SourceEdits.ApplyAsync(solution, changes, edit), KeyPrefix + index), diagnostic);
                }
            }
        }
    }

    private static string Signature(IMethodSymbol constructor) =>
        constructor.ContainingType.Name + "(" + string.Join(", ", constructor.Parameters.Select(static parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ")";

    // The attribute onto the constructor unless it has it, and off the others; null where one of the places cannot be edited.
    private static List<(DocumentId, TextChange)>? Plan(Solution solution, Compilation compilation, IMethodSymbol chosen, List<IMethodSymbol> candidates, CancellationToken token)
    {
        var changes = new List<(DocumentId, TextChange)>();
        if (!ConstructorBinder.IsMarked(chosen))
        {
            if (SourceEdits.Editable(solution, chosen.DeclaringSyntaxReferences.FirstOrDefault(), token) is not { } place)
            {
                return null;
            }

            var node = place.Node;
            var model = compilation.GetSemanticModel(node.SyntaxTree);
            changes.Add((place.Document.Id, SourceEdits.AddAttribute(model, node, WellKnownNames.ConstructorAttribute, node is TypeDeclarationSyntax ? "method" : null, token)));
        }

        foreach (var other in candidates.Where(candidate => !SymbolEqualityComparer.Default.Equals(candidate, chosen) && ConstructorBinder.IsMarked(candidate)))
        {
            if (SourceEdits.RemoveAttribute(solution, AttributeReader.Find(other, WellKnownNames.ConstructorAttribute)!.ApplicationSyntaxReference, token) is not { } removal)
            {
                return null;
            }

            changes.Add(removal);
        }

        return changes;
    }
}
