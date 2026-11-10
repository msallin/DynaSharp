using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of the analyzers that work out their edits themselves, as <see cref="SuggestedEdit"/>: one code action per edit a diagnostic carries, which writes its text over its span, so
/// the rest of the file keeps its layout and its line endings. Only an IDE loads this class.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>DYNA204</c>: the result of the earlier call assigned back to the variable, <c>q = q.Limit(1);</c>.</item>
/// <item><c>DYNA206</c>: the version of the item at 0, or <c>IgnoreVersion()</c> after <c>IfNotExists()</c>.</item>
/// <item><c>DYNA207</c>: each declared index, <c>Order.Indexes.ByStatus</c>, the closest spelling first.</item>
/// <item><c>DYNA208</c>: the members left out, added to <c>Project(...)</c>.</item>
/// <item><c>DYNA213</c>: each attribute of a close spelling.</item>
/// <item><c>DYNA224</c>: the builder of the key the query reads.</item>
/// <item><c>DYNA227</c>: <c>GetValueOrDefault()</c>, or <c>result.Found ? result.Value : default</c>.</item>
/// <item><c>DYNA233</c>: <c>Set</c> for <c>Add</c>.</item>
/// <item><c>DYNA235</c>: <c>IfVersion(n)</c> removed, or replaced with <c>IgnoreVersion()</c>.</item>
/// <item><c>DYNA241</c>: <c>IfExists()</c> after the <c>Update</c>.</item>
/// <item><c>DYNA242</c>: <c>Set</c> for <c>Add</c>, or the handle of a number under the attribute, <c>Expr.Attr&lt;long&gt;("expiresAt")</c>.</item>
/// </list>
/// The equivalence key is the rule and the rank of the edit, so a fix-all takes the edit of the same rank of every diagnostic, the closest spelling of each.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SuggestedEditCodeFix))]
[Shared]
internal sealed class SuggestedEditCodeFix : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
    [
        Descriptors.CommandReused.Id,
        Descriptors.IfNotExistsOnVersionedPut.Id,
        Descriptors.UndeclaredIndex.Id,
        Descriptors.ProjectionLeavesOutRequired.Id,
        Descriptors.PathMatchesNoAttribute.Id,
        Descriptors.KeyOfAnotherAttribute.Id,
        Descriptors.ValueWithoutFound.Id,
        Descriptors.AddWithoutNumberOrSet.Id,
        Descriptors.IfVersionOnPut.Id,
        Descriptors.UpdateCreatesUnreadableItem.Id,
        Descriptors.AddOnTimeToLive.Id,
    ];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        foreach (var diagnostic in context.Diagnostics)
        {
            var edits = SuggestedEdit.Of(diagnostic);
            for (var rank = 0; rank < edits.Count; rank++)
            {
                var edit = edits[rank];
                var key = diagnostic.Id + "." + rank.ToString(CultureInfo.InvariantCulture);
                context.RegisterCodeFix(CodeAction.Create(edit.Title, token => ApplyAsync(document, edit, token), key), diagnostic);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> ApplyAsync(Document document, SuggestedEdit edit, CancellationToken token)
    {
        var text = await document.GetTextAsync(token).ConfigureAwait(false);
        return document.WithText(text.WithChanges(new TextChange(edit.Span, edit.Text)));
    }
}
