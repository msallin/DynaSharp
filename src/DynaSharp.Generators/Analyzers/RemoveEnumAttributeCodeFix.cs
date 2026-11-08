using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA033 on the attribute: the <c>[DynamoDbEnum]</c> that changes nothing goes. On a property it is offered nowhere, since the attribute works for the other properties of the enum.
/// Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveEnumAttributeCodeFix))]
[Shared]
internal sealed class RemoveEnumAttributeCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every enum, so a fix-all removes each.</summary>
    internal const string Key = "DYNA033.Remove";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.IneffectiveEnumAttribute.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var text = await document.GetTextAsync(token).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            // The analyzer reports nothing in generated code.
            if (SourceEdits.AttributeAt(root, diagnostic.Location.SourceSpan) is { } attribute)
            {
                var removal = SourceEdits.RemoveAttribute(text, attribute);
                context.RegisterCodeFix(CodeAction.Create("Remove [DynamoDbEnum]", edit => SourceEdits.ApplyAsync(document, [removal], edit), Key), diagnostic);
            }
        }
    }
}
