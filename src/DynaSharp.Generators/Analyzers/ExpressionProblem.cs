namespace DynaSharp.Generators;

/// <summary>A problem in an expression.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Part">The index of the part it is in, or <see cref="WholeExpression"/>.</param>
/// <param name="Start">The offset in the text of the part.</param>
/// <param name="Length">The length in the text of the part, 0 for a hole.</param>
/// <param name="Detail">The text the message needs: the string, the placeholder or the reason.</param>
internal readonly record struct ExpressionProblem(ExpressionProblemKind Kind, int Part, int Start, int Length, string Detail)
{
    /// <summary>The part of a problem that has no place, such as an expression that ends too early.</summary>
    public const int WholeExpression = -1;
}
