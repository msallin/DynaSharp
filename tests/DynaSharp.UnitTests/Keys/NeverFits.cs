namespace DynaSharp.UnitTests.Keys;

/// <summary>A custom key type that never has room enough, whatever it is given.</summary>
internal readonly record struct NeverFits : IUtf8SpanFormattable
{
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        bytesWritten = 0;
        return false;
    }
}
