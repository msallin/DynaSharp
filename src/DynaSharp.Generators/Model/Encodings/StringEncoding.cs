namespace DynaSharp.Generators;

/// <summary>The text as is, compared by UTF-8 bytes.</summary>
/// <param name="IsRaw">
/// Whether this is format <c>raw</c>: delimiters are part of the value, which ends only where the key does. Everything else about a value stays, so U+10FFFF is still out, and an upper bound
/// still lies above every key under a prefix.
/// </param>
internal sealed record StringEncoding(bool IsRaw = false) : KeyEncoding(0)
{
    private const string NoDelimiters = "\"\"u8";

    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteString({value}, {Own(delimiters)}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) => $"{key}.TryReadString({Own(delimiters)}, out var {target})";

    private string Own(string delimiters) => IsRaw ? NoDelimiters : delimiters;
}
