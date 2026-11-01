namespace DynaSharp.Generators;

/// <summary>The text as is, compared by UTF-8 bytes.</summary>
internal sealed record StringEncoding() : KeyEncoding(0)
{
    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteString({value}, {delimiters}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) => $"{key}.TryReadString({delimiters}, out var {target})";
}
