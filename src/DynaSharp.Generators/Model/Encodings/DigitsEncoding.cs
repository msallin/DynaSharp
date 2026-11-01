namespace DynaSharp.Generators;

/// <summary>Format <c>d&lt;w&gt;</c>: a non-negative integer or enum value, zero-padded.</summary>
/// <param name="Width">The number of digits.</param>
/// <param name="Type">The integer type, for an enum its underlying type.</param>
/// <param name="ValueType">The placeholder's type as code, the enum or the integer keyword.</param>
internal sealed record DigitsEncoding(int Width, IntegerType Type, string ValueType) : KeyEncoding(Width)
{
    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteDigits(({Type.ReaderType}){value}, {Width}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) =>
        $"{key}.TryReadDigits({Width}, out {Type.ReaderType} {target}){Type.RangeCheck(target)}";

    public override string ReadValue(string target) => $"({ValueType}){target}";
}
