using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA203, the three ways its docs name: send it, <c>orders.Put(order);</c> becomes <c>await orders.Put(order).SendAsync();</c>; keep what the call returns, <c>query.Descending();</c>
/// becomes <c>query = query.Descending();</c>; or drop it on purpose, <c>_ = orders.Put(order);</c>. Only an IDE loads this class.
/// </summary>
/// <remarks>
/// <para>
/// The send is offered in an <c>async</c> member, outside a <c>lock</c>, where the command has a <c>SendAsync</c> that binds, and on a chain that starts at something other than a command. A chain that
/// starts at a command in a local is offered the assignment back instead, where the local takes what the call returns: sent there, the local would be used up, and its next use would throw.
/// </para>
/// <para>The discard is offered wherever <c>_</c> names nothing, which a lambda's parameter can.</para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DiscardedCommandCodeFix))]
[Shared]
internal sealed class DiscardedCommandCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that sends the command.</summary>
    internal const string SendKey = "DYNA203.Send";

    /// <summary>The equivalence key of the fix that assigns the command back to its local.</summary>
    internal const string AssignKey = "DYNA203.Assign";

    /// <summary>The equivalence key of the fix that discards the command.</summary>
    internal const string DiscardKey = "DYNA203.Discard";

    private const string Discard = "_";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.CommandDiscarded.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = (await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false))!;
        foreach (var diagnostic in context.Diagnostics)
        {
            var command = (InvocationExpressionSyntax)root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var start = ChainStart(command);
            if (DynaSharpTypes.IsCommand(model.GetTypeInfo(start, context.CancellationToken).Type))
            {
                if (start is IdentifierNameSyntax name
                    && command.Parent is ExpressionStatementSyntax
                    && model.GetSymbolInfo(name, context.CancellationToken).Symbol is ILocalSymbol local
                    && SymbolEqualityComparer.Default.Equals(local.Type, model.GetTypeInfo(command, context.CancellationToken).Type))
                {
                    Register(context, root, diagnostic, "Assign it back to '" + local.Name + "'", AssignKey, command, Assigned(local.Name, command));
                }
            }
            else if (CanAwait(model, command, context.CancellationToken))
            {
                var send = (InvocationExpressionSyntax)ChainSyntax.Append(command, ["SendAsync()"]);
                if (model.GetSpeculativeSymbolInfo(command.SpanStart, send, SpeculativeBindingOption.BindAsExpression).Symbol is not null)
                {
                    var awaited = SyntaxFactory.AwaitExpression(SyntaxFactory.Token(default, SyntaxKind.AwaitKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space)), send.WithoutLeadingTrivia())
                        .WithLeadingTrivia(send.GetLeadingTrivia());
                    Register(context, root, diagnostic, "Send it with await and SendAsync()", SendKey, command, awaited);
                }
            }

            if (model.LookupSymbols(command.SpanStart, name: Discard).IsEmpty)
            {
                Register(context, root, diagnostic, "Discard it with _ =", DiscardKey, command, Assigned(Discard, command));
            }
        }
    }

    private static void Register(CodeFixContext context, SyntaxNode root, Diagnostic diagnostic, string title, string key, ExpressionSyntax command, ExpressionSyntax replacement)
    {
        var document = context.Document;
        context.RegisterCodeFix(CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(command, replacement))), key), diagnostic);
    }

    // query.Descending().Limit(5) starts at query: down the receivers of the calls, extension methods of the library included, which are written as calls on their receiver.
    private static ExpressionSyntax ChainStart(ExpressionSyntax expression)
    {
        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
        {
            expression = access.Expression;
        }

        return expression;
    }

    // An await needs an async function around it, and no lock between.
    private static bool CanAwait(SemanticModel model, SyntaxNode command, CancellationToken token) =>
        model.GetEnclosingSymbol(command.SpanStart, token) is IMethodSymbol { IsAsync: true }
        && command.FirstAncestorOrSelf<SyntaxNode>(static node => node is LockStatementSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax) is not LockStatementSyntax;

    private static AssignmentExpressionSyntax Assigned(string name, ExpressionSyntax command) =>
        SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName(name).WithLeadingTrivia(command.GetLeadingTrivia()).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(default, SyntaxKind.EqualsToken, SyntaxFactory.TriviaList(SyntaxFactory.Space)),
            command.WithoutLeadingTrivia());
}
