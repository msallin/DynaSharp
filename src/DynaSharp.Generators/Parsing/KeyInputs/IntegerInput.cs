namespace DynaSharp.Generators;

/// <summary>Integers up to 64 bits: <c>plain</c> by default, <c>d&lt;w&gt;</c> and <c>n&lt;i&gt;</c>.</summary>
/// <param name="typeName">The type as messages show it.</param>
/// <param name="type">The integer type.</param>
internal sealed class IntegerInput(string typeName, IntegerType type) : KeyInputType(typeName)
{
    private const string Plain = "plain";

    // ulong.MaxValue has 20 digits, so a wider d<w> only pads.
    private const int MaxDigitsWidth = 20;

    private const int MaxFixedPointDigits = 28;

    public override FormatBinding Bind(string? format, string property)
    {
        if (format is null or Plain)
        {
            return Encoded(new PlainIntegerEncoding(type));
        }

        var number = ReadNumber(format.Substring(1));
        return format[0] switch
        {
            'd' => Width(format, number),
            'n' => FixedPoint(format, number),
            _ => DoesNotFit(format),
        };
    }

    private FormatBinding Width(string format, int width)
    {
        if (width < 0)
        {
            return DoesNotFit(format);
        }

        return width is 0 or > MaxDigitsWidth ? OutOfRange(format) : Encoded(new DigitsEncoding(width, type, type.Keyword));
    }

    private FormatBinding FixedPoint(string format, int digits)
    {
        if (digits < 0)
        {
            return DoesNotFit(format);
        }

        return digits is 0 or > MaxFixedPointDigits ? OutOfRange(format) : Encoded(new FixedPointEncoding(digits, 0, type));
    }
}
