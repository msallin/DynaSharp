using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA003: <c>[DynamoDbIgnore]</c> on a property whose type cannot be stored. The other way the message names, a converter, needs a type only the author can write. A parameter of a
/// positional record gets none: a constructor parameter of an ignored property would have nothing to bind to. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IgnorePropertyCodeFix))]
[Shared]
internal sealed class IgnorePropertyCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every property, so a fix-all ignores each.</summary>
    internal const string Key = "DYNA003.Ignore";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.UnsupportedType.Id];

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
        foreach (var diagnostic in context.Diagnostics)
        {
            if (SourceEdits.NodeAt(root, diagnostic.Location.SourceSpan).FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } property)
            {
                var change = SourceEdits.AddAttribute(model, property, WellKnownNames.IgnoreAttribute, null, token);
                context.RegisterCodeFix(CodeAction.Create("Add [DynamoDbIgnore]", edit => SourceEdits.ApplyAsync(document, [change], edit), Key), diagnostic);
            }
        }
    }
}
