namespace DynaSharp.Generators;

/// <summary>The stored name of the member, through the generated converter of the enum.</summary>
/// <param name="ConverterName">The generated converter, which has <c>FindName</c> and <c>TryParseName</c>.</param>
/// <param name="EnumType">The enum as code.</param>
/// <param name="StoredNames">The stored names of the enum, which the template checks against its delimiters.</param>
internal sealed record EnumNameEncoding(string ConverterName, string EnumType, EquatableArray<string> StoredNames) : KeyEncoding(0)
{
    // The key asks the generated converter for the name that the attribute holds.
    public override bool HasTextOf(string converter) => converter == ConverterName;

    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        $"{key}.WriteEnumName({ConverterName}.FindName({value}), {delimiters}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) =>
        $"{key}.TryReadUtf8Value({delimiters}, out var {target}Name) && {ConverterName}.TryParseName({target}Name, out {EnumType} {target})";
}
