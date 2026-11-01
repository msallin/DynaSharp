namespace DynaSharp.Expressions;

/// <summary>What joins the terms of a group in parentheses, which is what says whether a group inside it can give up its own.</summary>
internal enum ExpressionGroup
{
    /// <summary>No group, for what a term can be without one.</summary>
    None,

    /// <summary>The terms are joined by <c>AND</c>.</summary>
    And,

    /// <summary>The terms are joined by <c>OR</c>.</summary>
    Or,
}
