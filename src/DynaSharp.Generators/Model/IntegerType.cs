namespace DynaSharp.Generators;

/// <summary>An integer type of a key input, with the checks a read needs when the reader's type is wider.</summary>
/// <param name="Keyword">The C# keyword, for example <c>int</c>.</param>
/// <param name="IsUnsigned">Whether the reader's type is <c>ulong</c> rather than <c>long</c>.</param>
/// <param name="IsFullWidth">Whether the type is the reader's type.</param>
internal sealed record IntegerType(string Keyword, bool IsUnsigned, bool IsFullWidth)
{
    private static readonly Dictionary<string, int> Digits = new()
    {
        ["sbyte"] = 3,
        ["byte"] = 3,
        ["short"] = 5,
        ["ushort"] = 5,
        ["int"] = 10,
        ["uint"] = 10,
        ["long"] = 19,
        ["ulong"] = 20,
    };

    public string ReaderType => IsUnsigned ? "ulong" : "long";

    /// <summary>Gets the digits of the type's maximum, the width of a format that holds every value: 10 for <c>int</c>.</summary>
    public int MaxDigits => Digits[Keyword];

    // Input: "t". Output: " && t >= int.MinValue && t <= int.MaxValue", " && t <= byte.MaxValue", or nothing for long and ulong.
    public string RangeCheck(string target)
    {
        if (IsFullWidth)
        {
            return string.Empty;
        }

        return IsUnsigned ? $" && {target} <= {Keyword}.MaxValue" : FullRangeCheck(target);
    }

    // The same for a variable of type decimal, which is wider than every integer type on both sides.
    public string FullRangeCheck(string target) => $" && {target} >= {Keyword}.MinValue && {target} <= {Keyword}.MaxValue";
}
