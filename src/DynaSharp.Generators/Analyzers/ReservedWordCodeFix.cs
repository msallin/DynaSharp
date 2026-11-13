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
/// The fix of DYNA220: <c>$"Total > {100}"</c> becomes <c>$"{Attr("Total")} > {100}"</c>, with as many braces as a raw string's holes take, and <c>using static DynaSharp.Expr;</c> added if
/// <c>Attr</c> does not bind. The same for the DYNA212 of a bare name that starts with an underscore, <c>$"_type = {"Order"}"</c>. Only where the diagnostic stands on the word itself: in text
/// with an escape or a raw string's indent it stands on the whole text, and nothing is offered. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReservedWordCodeFix))]
[Shared]
internal sealed class ReservedWordCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every word, so that a fix-all puts each in a hole.</summary>
    internal const string Key = "DYNA220.Attr";

    // The edits of a document in one go, so that the import is added once: the batch fixer keeps one edit of each file that lacks it and drops the others, which add the same line.
    private static readonly FixAllProvider FixAll = FixAllProvider.Create(static async (context, document, diagnostics) =>
    {
        var root = (await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        return await InHolesAsync(document, [.. diagnostics.Select(diagnostic => Hole(root, diagnostic)).OfType<Edit>()], context.CancellationToken).ConfigureAwait(false);
    });

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.ReservedWordInExpression.Id, Descriptors.ExpressionSyntaxError.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => FixAll;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = (await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (Hole(root, diagnostic) is { } edit)
            {
                context.RegisterCodeFix(CodeAction.Create("Write " + edit.Hole, cancel => InHolesAsync(document, [edit], cancel), Key), diagnostic);
            }
        }
    }

    // Null where the diagnostic does not stand on the word alone, and for a syntax error that is not about a name starting with an underscore.
    private static Edit? Hole(SyntaxNode root, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.SourceSpan;
        var word = root.GetText().ToString(span);
        var isName = diagnostic.Id == Descriptors.ExpressionSyntaxError.Id
            ? word.StartsWith("_", StringComparison.Ordinal) && word.All(ExpressionLexer.IsWordCharacter)
            : word.All(static character => char.IsLetter(character));
        if (!isName)
        {
            return null;
        }

        // A hole takes as many braces as the string has dollars: one for $"..." and $@"...", two for $$"""...""".
        var start = ((InterpolatedStringExpressionSyntax)root.FindToken(span.Start).Parent!.Parent!).StringStartToken;
        var braces = start.Text.Count(static character => character == '$');
        var call = "Attr(\"" + word + "\")";
        return new Edit(span, new string('{', braces) + call + new string('}', braces), SyntaxFactory.ParseExpression(call));
    }

    private static async Task<Document> InHolesAsync(Document document, Edit[] edits, CancellationToken token)
    {
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var binds = edits.All(edit => SourceEdits.AttrBinds(model, edit.Span.Start, edit.Call));
        var text = (await document.GetTextAsync(token).ConfigureAwait(false)).WithChanges(edits.Select(static edit => new TextChange(edit.Span, edit.Hole)));
        var edited = document.WithText(text);
        return binds ? edited : edited.WithSyntaxRoot(SourceEdits.AddExprImport((CompilationUnitSyntax)(await edited.GetSyntaxRootAsync(token).ConfigureAwait(false))!));
    }

    /// <summary>A word, the hole that replaces it, and the call in the hole.</summary>
    private sealed record Edit(TextSpan Span, string Hole, ExpressionSyntax Call);
}
