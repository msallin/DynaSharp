using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DynaSharp.UnitTests.Keys;

/// <summary>A custom key type that parses any text to upper case, so that a lower case spelling does not format back to itself.</summary>
/// <param name="Text">The text.</param>
internal readonly record struct Shouting(string Text) : IUtf8SpanFormattable, IUtf8SpanParsable<Shouting>
{
    public static Shouting Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) =>
        new(Encoding.UTF8.GetString(utf8Text).ToUpperInvariant());

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out Shouting result)
    {
        result = Parse(utf8Text, provider);
        return true;
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        Encoding.UTF8.TryGetBytes(Text, utf8Destination, out bytesWritten);
}
