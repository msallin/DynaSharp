namespace DynaSharp.Generators;

/// <summary>The member name, through the generated converter of the enum.</summary>
/// <param name="ConverterName">The generated converter, which has <c>FindName</c> and <c>TryParseName</c>.</param>
/// <param name="EnumType">The enum as code.</param>
internal sealed record EnumNameEncoding(string ConverterName, string EnumType) : KeyEncoding(0)
{
    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteEnumName({ConverterName}.FindName({value}), {delimiters}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) =>
        $"{key}.TryReadUtf8Value({delimiters}, out var {target}Name) && {ConverterName}.TryParseName({target}Name, out {EnumType} {target})";
}
