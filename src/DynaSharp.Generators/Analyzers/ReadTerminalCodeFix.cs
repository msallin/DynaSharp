using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA202 and DYNA234. DYNA202: one per terminal of the read that fits where the read stands, <c>await shop.Query("a")</c> becomes <c>await shop.Query("a").ItemAsync()</c>, and
/// <c>shop.Query("a").SendAsync(ct)</c> becomes <c>shop.Query("a").ItemsAsync(ct)</c>, with the arguments it had. DYNA234: <c>orders.Query(pk).ToListAsync()</c> becomes
/// <c>orders.Query(pk).ItemsAsync().ToListAsync()</c>, or <c>ValuesAsync()</c> after <c>Select</c>, as the diagnostic says. Only an IDE loads this class.
/// </summary>
/// <remarks>
/// A terminal of a read is a method of its own whose name ends in <c>Async</c>, so a new one needs no list. One is offered where the chain binds with it, the calls after it included, and where
/// what the chain gives fits: under an <c>await</c> a value that can be awaited, as the collection of a <c>foreach</c> one that streams.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReadTerminalCodeFix))]
[Shared]
internal sealed class ReadTerminalCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix of DYNA234.</summary>
    internal const string ItemsKey = "DYNA234";

    /// <summary>The start of the equivalence key of a fix of DYNA202, which the name of the terminal ends, so that a fix-all gives every read the same terminal where it has one.</summary>
    internal const string TerminalKey = "DYNA202.";

    private const string Suffix = "Async";

    private const string GetAwaiter = "GetAwaiter";

    private const string GetAsyncEnumerator = "GetAsyncEnumerator";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.ReadCommandNotAwaitable.Id, Descriptors.ItemMethodOnRead.Id];

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
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (diagnostic.Id == Descriptors.ItemMethodOnRead.Id)
            {
                var terminal = diagnostic.Properties[ReadTerminalAnalyzer.TerminalProperty]!;
                var access = (MemberAccessExpressionSyntax)node.Parent!;
                var title = "Call " + terminal + "() before " + access.Name.Identifier.ValueText;
                Register(context, root, diagnostic, title, ItemsKey, access.Expression, ChainSyntax.Append(access.Expression, [terminal + "()"]));
                continue;
            }

            // await shop.Query("a"), on the await, or shop.Query("a").SendAsync(), on the name of the call, whose arguments the terminal takes.
            var (receiver, target, send) = node is AwaitExpressionSyntax awaited
                ? (awaited.Expression, awaited.Expression, null)
                : (((MemberAccessExpressionSyntax)node.Parent!).Expression, (ExpressionSyntax)node.Parent.Parent!, (SimpleNameSyntax)node);
            foreach (var terminal in model.GetTypeInfo(receiver, context.CancellationToken).Type!.GetMembers().Where(static member => member is IMethodSymbol && member.Name.EndsWith(Suffix, StringComparison.Ordinal)).Select(static member => member.Name).Distinct())
            {
                var call = send is null
                    ? (InvocationExpressionSyntax)ChainSyntax.Append(receiver, [terminal + "()"])
                    : ((InvocationExpressionSyntax)target).ReplaceNode(send, SyntaxFactory.IdentifierName(terminal).WithTriviaFrom(send));
                if (Fits(model, target, call))
                {
                    Register(context, root, diagnostic, "End the read with " + terminal + call.ArgumentList.WithoutTrivia().ToString(), TerminalKey + terminal, target, call);
                }
            }
        }
    }

    private static void Register(CodeFixContext context, SyntaxNode root, Diagnostic diagnostic, string title, string key, ExpressionSyntax target, ExpressionSyntax replacement)
    {
        var document = context.Document;
        context.RegisterCodeFix(CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(target, replacement))), key), diagnostic);
    }

    // Whether the chain binds with the terminal in it, up to its last call, and what that gives fits where it stands: under an await, a value that can be awaited, in a foreach, one that streams.
    private static bool Fits(SemanticModel model, ExpressionSyntax target, ExpressionSyntax call)
    {
        while (target.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax outer })
        {
            call = outer.ReplaceNode(target, call);
            target = outer;
        }

        if (model.GetSpeculativeSymbolInfo(target.SpanStart, call, SpeculativeBindingOption.BindAsExpression).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return target.Parent switch
        {
            AwaitExpressionSyntax => !method.ReturnType.GetMembers(GetAwaiter).IsEmpty,
            ForEachStatementSyntax => !method.ReturnType.GetMembers(GetAsyncEnumerator).IsEmpty,
            _ => true,
        };
    }
}
