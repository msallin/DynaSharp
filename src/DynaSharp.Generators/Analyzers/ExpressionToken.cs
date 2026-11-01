namespace DynaSharp.Generators;

/// <summary>A token, by its place, so that reading an expression needs no string per token.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Part">The index of the part it is in.</param>
/// <param name="Start">The offset in the text of the part.</param>
/// <param name="Length">The length.</param>
internal readonly record struct ExpressionToken(TokenKind Kind, int Part, int Start, int Length);
