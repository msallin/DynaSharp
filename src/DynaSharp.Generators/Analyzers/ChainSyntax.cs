using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>The edits of the code fixes that add a call to a chain, laid out as the chain is.</summary>
internal static class ChainSyntax
{
    /// <summary>
    /// <c>receiver.First().Second()</c>, each call on a line of its own, at the indent of the call that follows the receiver, when that call starts a line; otherwise all on the receiver's line.
    /// </summary>
    /// <param name="receiver">The expression in the tree to add the calls to, never the name of a member access.</param>
    /// <param name="calls">The calls as they are written after the dot: <c>IgnoreVersion()</c>, <c>Including&lt;Customer&gt;()</c>.</param>
    /// <returns>The expression that replaces the receiver.</returns>
    public static ExpressionSyntax Append(ExpressionSyntax receiver, IEnumerable<string> calls)
    {
        // The dot of the call after the receiver carries the indent of a chain with one call per line, and the receiver the line break before it, which a comment can precede.
        var trailing = receiver.GetTrailingTrivia();
        var lineEnd = trailing.LastOrDefault(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        var ownLine = receiver.Parent is MemberAccessExpressionSyntax && lineEnd != default;
        var indent = ownLine ? ((MemberAccessExpressionSyntax)receiver.Parent!).OperatorToken.LeadingTrivia : default;
        var chain = ownLine ? receiver : receiver.WithoutTrailingTrivia();
        foreach (var call in calls)
        {
            // Parsed, so that no elastic trivia leads the formatter of the IDE to respace what is around it.
            var parsed = (InvocationExpressionSyntax)SyntaxFactory.ParseExpression("_." + call);
            var access = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                chain,
                SyntaxFactory.Token(default, SyntaxKind.DotToken, default).WithLeadingTrivia(indent),
                ((MemberAccessExpressionSyntax)parsed.Expression).Name);
            chain = SyntaxFactory.InvocationExpression(access, parsed.ArgumentList).WithTrailingTrivia(ownLine ? SyntaxFactory.TriviaList(lineEnd) : default);
        }

        return ownLine ? chain : chain.WithTrailingTrivia(trailing);
    }
}
