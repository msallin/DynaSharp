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
/// The fixes of DYNA035, the two ways its message names: <c>public string? Note { get; set; } = "n/a";</c> loses its initializer, or a constructor parameter <c>string? note = "n/a"</c> gets
/// <c>= null</c>; or the property becomes non-nullable, with each declaration that writes its type. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NullInitialValueCodeFix))]
[Shared]
internal sealed class NullInitialValueCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that makes a missing attribute read as null: no initializer, or a default of null.</summary>
    internal const string NullKey = "DYNA035.Null";

    /// <summary>The equivalence key of the fix that makes the property non-nullable.</summary>
    internal const string NonNullableKey = "DYNA035.NonNullable";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NullReadsBackAsInitialValue.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        if (SourceEdits.IsGenerated(root.SyntaxTree, token))
        {
            return;
        }

        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var solution = document.Project.Solution;
        foreach (var diagnostic in context.Diagnostics)
        {
            // On the parameter whose default it is, or on the property; on the type where the property is in another project. A default of [DefaultParameterValue] has no = to edit.
            var node = SourceEdits.NodeAt(root, diagnostic.Location.SourceSpan);
            var (toNull, title, owner) = node switch
            {
                ParameterSyntax { Default: { } value } parameter => (new TextChange(value.Value.Span, "null"), "Make the default value null", model.GetDeclaredSymbol(parameter, token)!.ContainingType),
                PropertyDeclarationSyntax property => (new TextChange(TextSpan.FromBounds(property.Initializer!.GetFirstToken().GetPreviousToken().Span.End, property.SemicolonToken.Span.End), string.Empty), "Remove the initializer", model.GetDeclaredSymbol(property, token)!.ContainingType),
                _ => (default(TextChange), string.Empty, null),
            };
            if (owner is null)
            {
                continue;
            }

            context.RegisterCodeFix(CodeAction.Create(title, edit => SourceEdits.ApplyAsync(document, [toNull], edit), NullKey), diagnostic);
            var name = diagnostic.Properties[DiagnosticInfo.FixProperty]!;
            var questions = Questions(solution, SourceEdits.PropertyOf(owner, name), node, token);
            if (questions is not null)
            {
                context.RegisterCodeFix(CodeAction.Create("Make " + name + " non-nullable", edit => SourceEdits.ApplyAsync(solution, questions, edit), NonNullableKey), diagnostic);
            }
        }
    }

    // The ? of each type that the property and the parameter write, or null where one is not written so or cannot be edited.
    private static List<(DocumentId, TextChange)>? Questions(Solution solution, IPropertySymbol property, SyntaxNode at, CancellationToken token)
    {
        var changes = new List<(DocumentId, TextChange)>();
        foreach (var place in property.DeclaringSyntaxReferences.Select(reference => SourceEdits.Editable(solution, reference, token)).Append(SourceEdits.Editable(solution, at.GetReference(), token)))
        {
            var type = place?.Node switch
            {
                PropertyDeclarationSyntax declaration => declaration.Type,
                ParameterSyntax parameter => parameter.Type,
                _ => null,
            };
            if (type is not NullableTypeSyntax nullable)
            {
                return null;
            }

            changes.Add((place!.Value.Document.Id, new TextChange(nullable.QuestionToken.Span, string.Empty)));
        }

        return changes;
    }
}
