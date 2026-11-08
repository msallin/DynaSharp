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

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.StringInNamePosition.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            var document = context.Document;
            var span = diagnostic.Location.SourceSpan;
            context.RegisterCodeFix(CodeAction.Create(Title, token => WrapAsync(document, span, token), Title), diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> WrapAsync(Document document, Microsoft.CodeAnalysis.Text.TextSpan span, CancellationToken token)
    {
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var value = (ExpressionSyntax)root.FindNode(span, getInnermostNodeForTie: true);
        var wrapped = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(AttrMethod),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value.WithoutTrivia()))))
            .WithTriviaFrom(value);

        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var updated = root.ReplaceNode(value, wrapped);
        return document.WithSyntaxRoot(SourceEdits.AttrBinds(model, span.Start, wrapped) ? updated : SourceEdits.AddExprImport(updated));
    }
}
