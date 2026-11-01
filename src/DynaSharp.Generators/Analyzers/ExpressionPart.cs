namespace DynaSharp.Generators;

/// <summary>One part of an expression: literal text, or a hole.</summary>
/// <param name="Text">The literal text, or the value of a constant string; otherwise empty.</param>
/// <param name="Hole">What the part is.</param>
internal readonly record struct ExpressionPart(string Text, HoleKind Hole)
{
    /// <summary>Literal text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The part.</returns>
    public static ExpressionPart Literal(string text) => new(text, HoleKind.None);
}
