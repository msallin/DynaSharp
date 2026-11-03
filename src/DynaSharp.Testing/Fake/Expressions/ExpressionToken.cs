namespace DynaSharp.Testing.Fake;

/// <summary>A token of an expression: its kind and where it stands in the text, for the parser and for the <c>near</c> of a syntax error.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Start">The position of its first character.</param>
/// <param name="Length">The number of its characters, 0 for <see cref="ExpressionTokenKind.End"/>.</param>
internal readonly record struct ExpressionToken(ExpressionTokenKind Kind, int Start, int Length)
{
    /// <summary>Gets the position after its last character.</summary>
    public int End => Start + Length;
}
