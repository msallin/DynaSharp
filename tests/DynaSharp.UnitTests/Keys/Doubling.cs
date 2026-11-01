using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.UnitTests.Keys;

/// <summary>A custom key type that parses to something that formats differently from what was read.</summary>
internal readonly record struct Doubling(int Value) : IUtf8SpanFormattable, IUtf8SpanParsable<Doubling>
{
    public static Doubling Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => new(int.Parse(utf8Text, provider) * 2);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out Doubling result)
    {
        var parsed = int.TryParse(utf8Text, provider, out var value);
        result = new Doubling(value * 2);
        return parsed;
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        Value.TryFormat(utf8Destination, out bytesWritten, format, provider);
}
