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
/// The fixes of DYNA202, one per terminal of the read that fits where the read stands: <c>await shop.Query("a")</c> becomes <c>await shop.Query("a").ItemAsync()</c>, and
/// <c>shop.Query("a").SendAsync(ct)</c> becomes <c>shop.Query("a").ItemsAsync(ct)</c>, with the arguments it had. Only an IDE loads this class.
/// </summary>
/// <remarks>
/// A terminal of a read is a method of its own whose name ends in <c>Async</c>, so a new one needs no list. One is offered where the chain binds with it, the calls after it included, and where
/// what the chain gives fits: under an <c>await</c> a value that can be awaited, as the collection of a <c>foreach</c> one that streams. That takes the semantic model of the IDE, which is why this fix
/// is not a <see cref="SuggestedEdit"/> of the analyzer.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReadTerminalCodeFix))]
[Shared]
internal sealed class ReadTerminalCodeFix : CodeFixProvider
{
    /// <summary>The start of the equivalence key of a fix, which the name of the terminal ends, so that a fix-all gives every read the same terminal where it has one.</summary>
    internal const string TerminalKey = "DYNA202.";

    private const string Suffix = "Async";

    private const string GetAwaiter = "GetAwaiter";

    private const string GetAsyncEnumerator = "GetAsyncEnumerator";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.ReadCommandNotAwaitable.Id];

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
            // await shop.Query("a"), on the await, or shop.Query("a").SendAsync(), on the name of the call, whose arguments the terminal takes.
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var (receiver, target, send) = node is AwaitExpressionSyntax awaited
                ? (awaited.Expression, awaited.Expression, null)
                : (((MemberAccessExpressionSyntax)node.Parent!).Expression, (ExpressionSyntax)node.Parent.Parent!, (SimpleNameSyntax)node);
            foreach (var terminal in model.GetTypeInfo(receiver, context.CancellationToken).Type!.GetMembers().Where(static member => member is IMethodSymbol && member.Name.EndsWith(Suffix, StringComparison.Ordinal)).Select(static member => member.Name).Distinct())
            {
                var (call, change) = send is null
                    ? (ChainSyntax.WithCall(receiver, terminal + "()"), ChainSyntax.Append(receiver, [terminal + "()"]))
                    : (((InvocationExpressionSyntax)target).ReplaceNode(send, SyntaxFactory.IdentifierName(terminal)), new TextChange(send.Span, terminal));
                if (Fits(model, target, call))
                {
                    var title = "End the read with " + terminal + call.ArgumentList.WithoutTrivia().ToString();
                    context.RegisterCodeFix(CodeAction.Create(title, token => SourceEdits.ApplyAsync(document, [change], token), TerminalKey + terminal), diagnostic);
                }
            }
        }
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
