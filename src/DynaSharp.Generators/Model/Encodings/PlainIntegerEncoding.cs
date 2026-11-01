namespace DynaSharp.Generators;

/// <summary>Format <c>plain</c> for integers: plain digits, numeric order only within one width.</summary>
/// <param name="Type">The integer type.</param>
internal sealed record PlainIntegerEncoding(IntegerType Type) : KeyEncoding(0)
{
    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteInteger(({Type.ReaderType}){value}, {delimiters}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) =>
        $"{key}.{(Type.IsUnsigned ? "TryReadUInt64" : "TryReadInt64")}({delimiters}, out {Type.ReaderType} {target}){Type.RangeCheck(target)}";

    public override string ReadValue(string target) => $"({Type.Keyword}){target}";
}
