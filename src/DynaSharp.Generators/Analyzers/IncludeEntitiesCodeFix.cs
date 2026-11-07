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
/// The fix of DYNA240: <c>orders.CreateTable()</c> becomes <c>orders.CreateTable().Including&lt;Customer&gt;().Including&lt;Product&gt;()</c>, with the names the analyzer gave, each on a line of its own when the
/// chain has one call per line. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IncludeEntitiesCodeFix))]
[Shared]
internal sealed class IncludeEntitiesCodeFix : CodeFixProvider
{
    private const string Title = "Include the entities of the table";

    private const string IncludingClause = "Including";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.EntityLeftOutOfTable.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            var document = context.Document;
            var span = diagnostic.Location.SourceSpan;
            var names = diagnostic.Properties[LeftOutEntityAnalyzer.FixProperty]!.Split(LeftOutEntityAnalyzer.Separator);
            context.RegisterCodeFix(CodeAction.Create(Title, token => IncludeAsync(document, span, names, token), Title), diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> IncludeAsync(Document document, TextSpan span, string[] names, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var createTable = root.FindNode(span, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>()!;

        // The dot of the call after CreateTable() carries the indent of a chain with one call per line, and CreateTable() the line break before it.
        var indent = createTable.Parent is MemberAccessExpressionSyntax next ? next.OperatorToken.LeadingTrivia : default;
        var lineEnd = createTable.GetTrailingTrivia();
        ExpressionSyntax chain = createTable;
        foreach (var name in names)
        {
            var including = SyntaxFactory.GenericName(SyntaxFactory.Identifier(IncludingClause), SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.ParseTypeName(name))));
            var access = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, chain, SyntaxFactory.Token(SyntaxKind.DotToken).WithLeadingTrivia(indent), including);
            chain = SyntaxFactory.InvocationExpression(access).WithTrailingTrivia(lineEnd);
        }

        return document.WithSyntaxRoot(root.ReplaceNode(createTable, chain));
    }
}
