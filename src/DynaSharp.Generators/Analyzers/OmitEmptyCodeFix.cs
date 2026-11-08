using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA030: the <c>[OmitEmpty]</c> goes, or, for a string that feeds keys of indexes alone, those indexes get <c>Sparse = true</c>, which the diagnostic names with the id of the type in
/// its property <c>Fix</c>. Moving the property out of the keys, making it nullable or giving it a default, the other ways the docs name, change the model. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OmitEmptyCodeFix))]
[Shared]
internal sealed class OmitEmptyCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that removes the attribute.</summary>
    internal const string RemoveKey = "DYNA030.Remove";

    /// <summary>The equivalence key of the fix that makes the indexes sparse.</summary>
    internal const string SparseKey = "DYNA030.Sparse";

    private static readonly string[] IndexAttributes = [WellKnownNames.GlobalIndexKeyAttribute, WellKnownNames.LocalIndexKeyAttribute];

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.InvalidOmitEmpty.Id];

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
            if (SourceEdits.RemoveAttribute(solution, SourceEdits.AttributeAt(root, diagnostic.Location.SourceSpan)?.GetReference(), token) is { } removal)
            {
                context.RegisterCodeFix(CodeAction.Create("Remove [OmitEmpty]", edit => SourceEdits.ApplyAsync(solution, [removal], edit), RemoveKey), diagnostic);
            }

            if (diagnostic.Properties.TryGetValue(DiagnosticInfo.FixProperty, out var fix) && Sparse(solution, compilation, fix!, token) is { } sparse)
            {
                context.RegisterCodeFix(CodeAction.Create(sparse.Title, edit => SourceEdits.ApplyAsync(solution, sparse.Changes, edit), SparseKey), diagnostic);
            }
        }
    }

    // Sparse = true on each index the diagnostic names, or null where one cannot be edited.
    private static (string Title, List<(DocumentId, Microsoft.CodeAnalysis.Text.TextChange)> Changes)? Sparse(Solution solution, Compilation compilation, string fix, CancellationToken token)
    {
        var parts = fix.Split(' ');
        var type = DocumentationCommentId.GetFirstSymbolForDeclarationId(parts[0], compilation)!;
        var names = parts.Skip(1).ToList();
        var changes = new List<(DocumentId, Microsoft.CodeAnalysis.Text.TextChange)>();
        foreach (var name in names)
        {
            var attribute = type.GetAttributes().First(found => IndexAttributes.Any(index => AttributeReader.IsNamed(found.AttributeClass, index)) && AttributeReader.Text(found, 0) == name);
            if (SourceEdits.Editable(solution, attribute.ApplicationSyntaxReference, token) is not { } place)
            {
                return null;
            }

            changes.Add((place.Document.Id, SourceEdits.SetNamedArgument((AttributeSyntax)place.Node, "Sparse", "true")));
        }

        return ((names.Count == 1 ? "Make index " : "Make indexes ") + string.Join(" and ", names) + " sparse", changes);
    }
}
