namespace DynaSharp.UnitTests.Keys;

/// <summary>A custom key type that writes exactly the given bytes, well-formed UTF-8 or not.</summary>
/// <param name="Bytes">The bytes.</param>
internal readonly record struct RawBytes(byte[] Bytes) : IUtf8SpanFormattable
{
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        bytesWritten = 0;
        if (Bytes.Length > utf8Destination.Length)
        {
            return false;
        }

        Bytes.CopyTo(utf8Destination);
        bytesWritten = Bytes.Length;
        return true;
    }
}
