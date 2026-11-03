namespace DynaSharp.Generators;

/// <summary>Lowercase <c>D</c> format, 36 bytes.</summary>
internal sealed record GuidEncoding() : KeyEncoding(36)
{
    public override bool CanReject => false;

    public override bool HasTextOf(string converter) => converter == Serialization + "GuidConverter";

    public override string WriteCall(string key, string value, string delimiters, string placeholder) => $"{key}.WriteGuid({value})";

    public override string TryReadCall(string key, string target, string delimiters) => $"{key}.TryReadGuid(out global::System.Guid {target})";
}
