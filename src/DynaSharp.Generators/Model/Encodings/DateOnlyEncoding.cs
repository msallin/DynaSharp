namespace DynaSharp.Generators;

/// <summary><c>yyyy-MM-dd</c>, 10 bytes.</summary>
internal sealed record DateOnlyEncoding() : KeyEncoding(10)
{
    public override bool CanReject => false;

    public override bool MirrorsAttribute => true;

    public override bool HasTextOf(string converter) => converter == Serialization + "DateOnlyConverter";

    public override string WriteCall(string key, string value, string delimiters, string placeholder) => $"{key}.WriteDate({value})";

    public override string TryReadCall(string key, string target, string delimiters) => $"{key}.TryReadDate(out global::System.DateOnly {target})";
}
