using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>The edits that add a call to a chain, laid out as the chain is.</summary>
internal static class ChainSyntax
{
    /// <summary>
    /// <c>receiver.First().Second()</c>, as an insertion that leaves the rest of the line alone: each call on a line of its own where the chain has one call per line, at the indent of the call that
    /// follows the receiver when that one starts a line, or else of the receiver's own call when that one does; otherwise all on the receiver's line. A comment at the end of the receiver's line stays there.
    /// </summary>
    /// <param name="receiver">The expression in the tree to add the calls to, never the name of a member access.</param>
    /// <param name="calls">The calls as they are written after the dot: <c>IgnoreVersion()</c>, <c>Including&lt;Customer&gt;()</c>.</param>
    /// <returns>The text to insert, and where.</returns>
    public static TextChange Append(ExpressionSyntax receiver, IEnumerable<string> calls)
    {
        if (receiver.Parent is MemberAccessExpressionSyntax next && LineEndAfter(receiver.GetLastToken()) is { } lineEnd)
        {
            var indent = next.OperatorToken.LeadingTrivia.ToFullString();
            return new TextChange(new TextSpan(lineEnd.Span.End, 0), string.Concat(calls.Select(call => indent + "." + call + lineEnd.ToFullString())));
        }

        var start = receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax own } && LineEndAfter(own.OperatorToken.GetPreviousToken()) is { } ownLineEnd
            ? ownLineEnd.ToFullString() + own.OperatorToken.LeadingTrivia.ToFullString()
            : string.Empty;
        return new TextChange(new TextSpan(receiver.Span.End, 0), string.Concat(calls.Select(call => start + "." + call)));
    }

    /// <summary>The receiver with a call after it, to bind where it stands; its layout is not the one <see cref="Append"/> writes.</summary>
    /// <param name="receiver">The expression in the tree.</param>
    /// <param name="call">The call as it is written after the dot.</param>
    /// <returns>The call.</returns>
    public static InvocationExpressionSyntax WithCall(ExpressionSyntax receiver, string call) =>
        (InvocationExpressionSyntax)SyntaxFactory.ParseExpression(receiver.WithoutTrivia().ToFullString() + "." + call);

    private static SyntaxTrivia? LineEndAfter(SyntaxToken token)
    {
        var lineEnd = token.TrailingTrivia.LastOrDefault(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        return lineEnd == default ? null : lineEnd;
    }
}
