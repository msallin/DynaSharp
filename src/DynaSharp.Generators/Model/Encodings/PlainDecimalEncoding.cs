namespace DynaSharp.Generators;

/// <summary>Format <c>plain</c> for <c>decimal</c>: the shortest form, so <c>1.0m</c> and <c>1m</c> are one key.</summary>
internal sealed record PlainDecimalEncoding() : KeyEncoding(0)
{
    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteDecimal({value}, {delimiters}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) => $"{key}.TryReadDecimal({delimiters}, out decimal {target})";
}
