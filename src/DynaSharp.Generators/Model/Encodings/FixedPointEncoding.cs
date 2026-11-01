namespace DynaSharp.Generators;

/// <summary>
/// Formats <c>n&lt;i&gt;.&lt;f&gt;</c> and <c>n&lt;i&gt;</c>: a sign letter, then digits, so byte order is numeric order. Negative values are
/// the nine's complement.
/// </summary>
/// <param name="IntegerDigits">The digits before the point.</param>
/// <param name="FractionDigits">The digits after the point, 0 for no point.</param>
/// <param name="Type">The integer type of the input, or null for <c>decimal</c>.</param>
internal sealed record FixedPointEncoding(int IntegerDigits, int FractionDigits, IntegerType? Type)
    : KeyEncoding(1 + IntegerDigits + (FractionDigits == 0 ? 0 : 1 + FractionDigits))
{
    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteFixedPoint({value}, {IntegerDigits}, {FractionDigits}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) =>
        $"{key}.TryReadFixedPoint({IntegerDigits}, {FractionDigits}, out decimal {target}){Type?.FullRangeCheck(target)}";

    public override string ReadValue(string target) => Type is null ? target : $"({Type.Keyword}){target}";
}
