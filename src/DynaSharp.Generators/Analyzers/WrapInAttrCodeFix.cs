using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA210: <c>$"{"status"} = {"open"}"</c> becomes <c>$"{Attr("status")} = {"open"}"</c>, with <c>using static DynaSharp.Expr;</c> added
/// if the file lacks it. Only an IDE loads this class, which is why the rest of the assembly can run in a compiler without the Workspaces dll.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WrapInAttrCodeFix))]
[Shared]
internal sealed class WrapInAttrCodeFix : CodeFixProvider
{
    private const string Title = "Wrap in Attr(...)";

    private const string AttrMethod = "Attr";

    // The wraps of a document in one go, so that the import is added once: the batch fixer keeps one edit of each file that lacks it and drops the others, which add the same line.
    private static readonly FixAllProvider FixAll = FixAllProvider.Create(static async (context, document, diagnostics) =>
        await WrapAsync(document, [.. diagnostics.Select(static diagnostic => diagnostic.Location.SourceSpan)], context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.StringInNamePosition.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => FixAll;

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            var document = context.Document;
            var span = diagnostic.Location.SourceSpan;
            context.RegisterCodeFix(CodeAction.Create(Title, token => WrapAsync(document, [span], token), Title), diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> WrapAsync(Document document, Microsoft.CodeAnalysis.Text.TextSpan[] spans, CancellationToken token)
    {
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var values = spans.Select(span => (ExpressionSyntax)root.FindNode(span, getInnermostNodeForTie: true)).ToList();
        var binds = true;
        var updated = root.ReplaceNodes(values, (value, _) =>
        {
            var wrapped = Wrapped(value);
            binds &= SourceEdits.AttrBinds(model, value.SpanStart, wrapped);
            return wrapped;
        });
        return document.WithSyntaxRoot(binds ? updated : SourceEdits.AddExprImport(updated));
    }

    private static InvocationExpressionSyntax Wrapped(ExpressionSyntax value) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(AttrMethod),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value.WithoutTrivia()))))
            .WithTriviaFrom(value);
}
