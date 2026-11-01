using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DynaSharp.UnitTests.Keys;

/// <summary>A custom key type that writes exactly the given text and parses any text unchanged.</summary>
/// <param name="Text">The text.</param>
internal readonly record struct RawText(string Text) : IUtf8SpanFormattable, IUtf8SpanParsable<RawText>
{
    public static RawText Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => new(Encoding.UTF8.GetString(utf8Text));

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out RawText result)
    {
        result = Parse(utf8Text, provider);
        return true;
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        new RawBytes(Encoding.UTF8.GetBytes(Text)).TryFormat(utf8Destination, out bytesWritten, format, provider);
}
