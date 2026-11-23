namespace DynaSharp.Expressions;

/// <summary>The text of the comparison operators.</summary>
internal static class ComparisonOperatorText
{
    /// <summary>Gets the operator with a space on either side, as it stands between two operands.</summary>
    /// <param name="comparison">The operator.</param>
    /// <returns>The text, for example <c> &lt;= </c>.</returns>
    internal static ReadOnlySpan<byte> Of(ComparisonOperator comparison) => comparison switch
    {
        ComparisonOperator.Equal => " = "u8,
        ComparisonOperator.NotEqual => " <> "u8,
        ComparisonOperator.Less => " < "u8,
        ComparisonOperator.LessOrEqual => " <= "u8,
        ComparisonOperator.Greater => " > "u8,
        _ => " >= "u8,
    };
}
