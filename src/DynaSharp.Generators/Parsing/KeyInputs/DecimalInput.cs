namespace DynaSharp.Generators;

/// <summary><c>decimal</c>: <c>plain</c> by default, and <c>n&lt;i&gt;.&lt;f&gt;</c> with <c>1 &lt;= i</c> and <c>i + f &lt;= 28</c>.</summary>
/// <param name="typeName">The type as messages show it.</param>
internal sealed class DecimalInput(string typeName) : KeyInputType(typeName)
{
    private const string Plain = "plain";

    private const int MaxDigits = 28;

    public override FormatBinding Bind(string? format, string property)
    {
        if (format is null or Plain)
        {
            return Encoded(new PlainDecimalEncoding());
        }

        // n10.2 gives 10 and 2.
        var point = format.IndexOf('.');
        if (format[0] != 'n' || point < 0)
        {
            return DoesNotFit(format);
        }

        var integerDigits = ReadNumber(format.Substring(1, point - 1));
        var fractionDigits = ReadNumber(format.Substring(point + 1));
        if (integerDigits < 0 || fractionDigits < 0)
        {
            return DoesNotFit(format);
        }

        return integerDigits is 0 or > MaxDigits || fractionDigits > MaxDigits - integerDigits
            ? OutOfRange(format)
            : Encoded(new FixedPointEncoding(integerDigits, fractionDigits, null));
    }
}
