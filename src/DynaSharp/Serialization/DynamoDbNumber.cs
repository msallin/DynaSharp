using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DynaSharp.Serialization;

/// <summary>
/// The rules for DynamoDB numbers, which travel as text: invariant culture, default (shortest round-trip) format,
/// no NaN or infinity.
/// </summary>
internal static class DynamoDbNumber
{
    // Integer types accept "1E+2" or "1.0" as long as the value is integral. No whitespace: DynamoDB numbers have none.
    private const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    internal static void ThrowIfNotFinite<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumberBase<T>
    {
        if (!T.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "DynamoDB cannot store NaN or infinity.");
        }
    }

    // The arguments are locals on purpose: with a property call or a temporary among them, the compiler guards against
    // `value` changing during argument evaluation and copies it for reference types, a branch number types never take.
    internal static string Format<T>(T value)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        return value.ToString(null, invariant);
    }

    internal static bool TryFormat<T>(T value, Span<byte> utf8Destination, out int bytesWritten)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        ReadOnlySpan<char> format = default;
        return value.TryFormat(utf8Destination, out bytesWritten, format, invariant);
    }

    internal static T Parse<T>(ReadOnlySpan<byte> utf8Text)
        where T : INumberBase<T>
        => T.Parse(utf8Text, Styles, CultureInfo.InvariantCulture);

    internal static T Parse<T>(ReadOnlySpan<char> text)
        where T : INumberBase<T>
        => T.Parse(text, Styles, CultureInfo.InvariantCulture);

    // Parse throws a FormatException that quotes the text; converters must not leak values into messages, so they ask this instead.
    internal static bool TryParse<T>(ReadOnlySpan<byte> utf8Text, [MaybeNullWhen(false)] out T value)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        return T.TryParse(utf8Text, Styles, invariant, out value);
    }

    internal static bool TryParse<T>(ReadOnlySpan<char> text, [MaybeNullWhen(false)] out T value)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        return T.TryParse(text, Styles, invariant, out value);
    }
}
