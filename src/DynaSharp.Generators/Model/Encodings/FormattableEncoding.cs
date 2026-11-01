namespace DynaSharp.Generators;

/// <summary>A type with <c>IUtf8SpanFormattable</c> and <c>IUtf8SpanParsable&lt;T&gt;</c>, by its own invariant text.</summary>
/// <param name="ValueType">The type as code.</param>
internal sealed record FormattableEncoding(string ValueType) : KeyEncoding(0)
{
    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteFormattable({value}, {delimiters}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) =>
        $"{key}.TryReadParsable<{ValueType}>({delimiters}, out var {target})";
}
