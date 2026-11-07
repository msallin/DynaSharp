using System.Numerics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;

namespace DynaSharp.UnitTests.Properties;

/// <summary>
/// Generators for the property tests. Each one covers its whole domain, edges included, rather than the small values FsCheck draws by
/// default: a number of every magnitude, text of every plane, values nested in each other.
/// </summary>
internal static class Generate
{
    // Characters JSON, keys and expressions treat specially, and the edges of UTF-16 and UTF-8.
    private static readonly int[] SpecialScalars =
    [
        0x00, 0x01, 0x08, 0x09, 0x0A, 0x0D, 0x1F, ' ', '"', '#', '\'', '+', '-', '.', '/', ':', '<', '>', '&', '[', ']', '\\', '{', '}', 0x7F,
        0x80, 0xA0, 0xAD, 0xE9, 0x7FF, 0x800, 0x2028, 0x2029, 0x20AC, 0xD7FF, 0xE000, 0xFEFF, 0xFFFD, 0xFFFE, 0xFFFF, 0x10000, 0x1F600, 0x10FFFF,
    ];

    /// <summary>Gets text that is valid UTF-16, of any plane, control characters and the characters that need escaping included.</summary>
    public static Gen<string> Text { get; } = Gen.Sized(size => Gen.ListOf(Scalar(), Math.Min(size, 24)).Select(Join));

    /// <summary>Gets text that is valid UTF-16 and not empty.</summary>
    public static Gen<string> NonEmptyText { get; } = Gen.Sized(size => Gen.NonEmptyListOf(Scalar()).Resize(Math.Clamp(size, 1, 24)).Select(Join));

    /// <summary>Gets text with one surrogate that is not part of a pair, somewhere in valid text.</summary>
    public static Gen<string> TextWithLoneSurrogate { get; } =
        from before in Text
        from surrogate in Gen.OneOf(Gen.Choose(0xD800, 0xDBFF), Gen.Choose(0xDC00, 0xDFFF))
        from after in Text
        where !(char.IsHighSurrogate((char)surrogate) && after.Length > 0 && char.IsLowSurrogate(after[0]))
            && !(char.IsLowSurrogate((char)surrogate) && before.Length > 0 && char.IsHighSurrogate(before[^1]))
        select before + (char)surrogate + after;

    /// <summary>Gets a <see cref="long"/> of any magnitude, the limits included.</summary>
    public static Gen<long> Int64 { get; } = Gen.Frequency(
        (1, Gen.Elements(long.MinValue, long.MinValue + 1, -1L, 0L, 1L, long.MaxValue - 1, long.MaxValue)),
        (5, from bits in RandomBits() from shift in Gen.Choose(0, 63) select (long)bits >> shift));

    /// <summary>Gets a <see cref="ulong"/> of any magnitude, the limits included.</summary>
    public static Gen<ulong> UInt64 { get; } = Gen.Frequency(
        (1, Gen.Elements(0UL, 1UL, ulong.MaxValue - 1, ulong.MaxValue, (ulong)long.MaxValue + 1)),
        (5, from bits in RandomBits() from shift in Gen.Choose(0, 63) select bits >> shift));

    /// <summary>Gets a <see cref="decimal"/> of any coefficient up to 96 bits and any scale up to 28, the limits included.</summary>
    public static Gen<decimal> Decimal { get; } = Gen.Frequency(
        (1, Gen.Elements(decimal.MinValue, decimal.MaxValue, 0m, -0m, 1m, -1m, 0.0000000000000000000000000001m, 1.0000000000000000000000000001m, 12.50m)),
        (5, from low in RandomBits()
            from high in RandomBits()
            from width in Gen.Choose(0, 96)
            from negative in Gen.Elements(false, true)
            from scale in Gen.Choose(0, 28)
            select Compose(low, high, width, negative, scale)));

    /// <summary>Gets a finite <see cref="double"/> of any bit pattern, subnormals and negative zero included.</summary>
    public static Gen<double> Double { get; } = Gen.Frequency(
        (1, Gen.Elements(0d, -0d, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue, 0.1, 1d / 3, 1E-130, 9.999999999999999E+125, 123456789012345680d)),
        (2, Gen.Choose(-1_000_000, 1_000_000).Select(static whole => whole / 100d)),
        (5, RandomBits().Select(static bits => BitConverter.Int64BitsToDouble((long)bits)).Where(double.IsFinite)));

    /// <summary>Gets a finite <see cref="float"/> of any bit pattern, subnormals and negative zero included.</summary>
    public static Gen<float> Single { get; } = Gen.Frequency(
        (1, Gen.Elements(0f, -0f, float.Epsilon, float.MaxValue, float.MinValue, 0.1f, 1f / 3, 16777217f)),
        (5, RandomBits().Select(static bits => BitConverter.Int32BitsToSingle((int)bits)).Where(float.IsFinite)));

    /// <summary>Gets a <see cref="DateTime"/> in UTC anywhere in its range, at the resolution of a tick.</summary>
    public static Gen<DateTime> UtcDateTime { get; } = Ticks(DateTime.MaxValue.Ticks).Select(static ticks => new DateTime(ticks, DateTimeKind.Utc));

    /// <summary>Gets a <see cref="DateTimeOffset"/> anywhere in its range, with any offset DateTimeOffset allows.</summary>
    public static Gen<DateTimeOffset> DateTimeOffset { get; } =
        from ticks in Ticks(DateTime.MaxValue.Ticks)
        from minutes in Gen.Frequency((1, Gen.Constant(0)), (3, Gen.Choose(-14 * 60, 14 * 60)))
        let offset = System.TimeSpan.FromMinutes(minutes)
        where ticks + offset.Ticks >= 0 && ticks + offset.Ticks <= DateTime.MaxValue.Ticks
        select new DateTimeOffset(ticks + offset.Ticks, offset);

    /// <summary>Gets a <see cref="TimeSpan"/> of any length, negative ones and the limits included.</summary>
    public static Gen<TimeSpan> TimeSpan { get; } = Gen.Frequency(
        (1, Gen.Elements(System.TimeSpan.MinValue, System.TimeSpan.MaxValue, System.TimeSpan.Zero, System.TimeSpan.FromTicks(-1))),
        (5, Int64.Select(System.TimeSpan.FromTicks)));

    /// <summary>Gets a <see cref="DateOnly"/> anywhere in its range.</summary>
    public static Gen<DateOnly> DateOnly { get; } = Ticks(System.DateOnly.MaxValue.DayNumber).Select(static day => System.DateOnly.FromDayNumber((int)day));

    /// <summary>Gets a <see cref="TimeOnly"/> anywhere in a day, at the resolution of a tick.</summary>
    public static Gen<TimeOnly> TimeOnly { get; } = Ticks(System.TimeOnly.MaxValue.Ticks).Select(static ticks => new TimeOnly(ticks));

    /// <summary>Gets any <see cref="Guid"/>.</summary>
    public static Gen<Guid> Guid { get; } = Gen.ArrayOf(Byte(), 16).Select(static bytes => new Guid(bytes));

    /// <summary>Gets bytes, possibly none.</summary>
    public static Gen<byte[]> Bytes { get; } = Gen.Sized(size => Gen.ArrayOf(Byte(), Math.Min(size, 40)));

    /// <summary>Gets the text of a number in the grammar of JSON, as DynamoDB stores it: up to 38 digits, an exponent in its range.</summary>
    public static Gen<string> NumberText { get; } =
        from negative in Gen.Elements(string.Empty, "-")
        from units in Gen.Frequency((1, Gen.Constant("0")), (4, DigitsWithoutLeadingZero(1, 20)))
        from fraction in Gen.Frequency((1, Gen.Constant(string.Empty)), (1, Digits(1, 18).Select(static digits => "." + digits)))
        from exponent in Gen.Frequency((2, Gen.Constant(string.Empty)), (1, Gen.Choose(-100, 100).Select(static e => "E" + (e < 0 ? "-" : "+") + Math.Abs(e))))
        select negative + units + fraction + exponent;

    /// <summary>Gets an attribute value of any type, nested up to a depth that the size decides.</summary>
    public static Gen<AttributeValue> AttributeValue { get; } = Gen.Sized(static size => Value(Math.Min(size, 40), plainJson: false));

    /// <summary>Gets an attribute value that plain JSON holds as well: no sets, no binary, and no null inside a map, which JSON leaves out.</summary>
    public static Gen<AttributeValue> JsonShapedValue { get; } = Gen.Sized(static size => Value(Math.Min(size, 40), plainJson: true));

    /// <summary>Gets a string of the given number of decimal digits, leading zeros allowed.</summary>
    /// <param name="min">The fewest digits.</param>
    /// <param name="max">The most digits.</param>
    /// <returns>The generator.</returns>
    public static Gen<string> Digits(int min, int max) =>
        from length in Gen.Choose(min, max)
        from digits in Gen.ArrayOf(Gen.Choose('0', '9').Select(static digit => (char)digit), length)
        select new string(digits);

    /// <summary>Shrinks text towards shorter text, a whole scalar at a time, so that every candidate is valid UTF-16 if the text was.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The candidates, smallest first.</returns>
    public static IEnumerable<string> ShrinkText(string text)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        yield return string.Empty;
        var scalars = Scalars(text);
        if (scalars.Count > 1)
        {
            yield return Join(scalars[..(scalars.Count / 2)]);
            yield return Join(scalars[(scalars.Count / 2)..]);
        }

        for (var i = 0; i < scalars.Count && i < 24; i++)
        {
            yield return Join([.. scalars[..i], .. scalars[(i + 1)..]]);
        }
    }

    /// <summary>Shrinks an attribute value towards its parts and towards fewer elements, keeping every candidate a value DynamoDB stores.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The candidates.</returns>
    public static IEnumerable<AttributeValue> ShrinkAttributeValue(AttributeValue value)
    {
        switch (value.Type)
        {
            case AttributeType.String:
                foreach (var shorter in ShrinkText(value.AsString()))
                {
                    yield return DynaSharp.AttributeValue.FromString(shorter);
                }

                break;
            case AttributeType.List:
                var elements = value.AsList();
                foreach (var element in elements)
                {
                    yield return element;
                }

                for (var i = 0; i < elements.Count; i++)
                {
                    yield return DynaSharp.AttributeValue.FromList([.. elements.Take(i), .. elements.Skip(i + 1)]);
                }

                break;
            case AttributeType.Map:
                var entries = value.AsMap().ToList();
                foreach (var entry in entries)
                {
                    yield return entry.Value;
                }

                for (var i = 0; i < entries.Count; i++)
                {
                    yield return DynaSharp.AttributeValue.FromMap([.. entries.Take(i), .. entries.Skip(i + 1)]);
                }

                break;
            case AttributeType.StringSet when value.AsStringSet().Count > 1:
                yield return DynaSharp.AttributeValue.FromStringSet(value.AsStringSet().Skip(1));
                break;
            case AttributeType.NumberSet when value.AsNumberSet().Count > 1:
                yield return DynaSharp.AttributeValue.FromNumberSet(value.AsNumberSet().Skip(1));
                break;
            case AttributeType.BinarySet when value.AsBinarySet().Count > 1:
                yield return DynaSharp.AttributeValue.FromBinarySet(value.AsBinarySet().Skip(1).Select(static member => member.ToArray()));
                break;
            default:
                break;
        }
    }

    private static Gen<AttributeValue> Value(int size, bool plainJson)
    {
        Gen<AttributeValue>[] json =
        [
            Text.Select(DynaSharp.AttributeValue.FromString),
            NumberText.Select(DynaSharp.AttributeValue.FromNumber),
            Gen.Elements(DynaSharp.AttributeValue.FromBool(true), DynaSharp.AttributeValue.FromBool(false)),
        ];
        Gen<AttributeValue>[] native =
        [
            Bytes.Select(static bytes => DynaSharp.AttributeValue.FromBinary(bytes)),
            Gen.Constant(DynaSharp.AttributeValue.Null),
            Distinct(Text, StringComparer.Ordinal).Select(DynaSharp.AttributeValue.FromStringSet),
            Distinct(NumberText, StringComparer.Ordinal).Select(DynaSharp.AttributeValue.FromNumberSet),
            Distinct(Bytes, ByteContent.Instance).Select(static members => DynaSharp.AttributeValue.FromBinarySet(members)),
        ];
        var scalar = Gen.OneOf(plainJson ? json : [.. json, .. native]);
        if (size <= 1)
        {
            return scalar;
        }

        var element = Value(size / 3, plainJson);
        var listElement = plainJson ? Gen.Frequency((6, element), (1, Gen.Constant(DynaSharp.AttributeValue.Null))) : element;
        return Gen.Frequency(
            (4, scalar),
            (1, Gen.ListOf(listElement, 4).Select(static elements => DynaSharp.AttributeValue.FromList(elements))),
            (1, Gen.ListOf(Gen.Zip(NonEmptyText, element), 4).Select(static entries =>
                DynaSharp.AttributeValue.FromMap(entries.DistinctBy(static entry => entry.Item1, StringComparer.Ordinal).Select(static entry => KeyValuePair.Create(entry.Item1, entry.Item2))))));
    }

    private static Gen<T[]> Distinct<T>(Gen<T> members, IEqualityComparer<T> comparer) =>
        Gen.NonEmptyListOf(members).Resize(5).Select(list => list.Distinct(comparer).ToArray());

    private static Gen<int> Scalar() => Gen.Frequency(
        (6, Gen.Choose(0x20, 0x7E)),
        (3, Gen.Elements(SpecialScalars)),
        (2, Gen.Choose(0x80, 0xD7FF)),
        (1, Gen.Choose(0xE000, 0xFFFF)),
        (1, Gen.Choose(0x10000, 0x10FFFF)));

    private static Gen<byte> Byte() => Gen.Choose(0, 255).Select(static value => (byte)value);

    // 64 bits of two draws, since Gen.Choose takes an int.
    private static Gen<ulong> RandomBits() =>
        from high in Gen.Choose(int.MinValue, int.MaxValue)
        from low in Gen.Choose(int.MinValue, int.MaxValue)
        select ((ulong)(uint)high << 32) | (uint)low;

    // From 0 to max, inclusive, both ends drawn often.
    private static Gen<long> Ticks(long max) => Gen.Frequency(
        (1, Gen.Elements(0L, max)),
        (5, RandomBits().Select(bits => (long)(bits % ((ulong)max + 1)))));

    private static Gen<string> DigitsWithoutLeadingZero(int min, int max) =>
        from first in Gen.Choose('1', '9')
        from rest in Digits(min - 1, max - 1)
        select (char)first + rest;

    private static decimal Compose(ulong low, ulong high, int width, bool negative, int scale)
    {
        var coefficient = ((new BigInteger(high) << 64) | low) & ((BigInteger.One << width) - 1);
        var lo = (int)(uint)(coefficient & uint.MaxValue);
        var mid = (int)(uint)((coefficient >> 32) & uint.MaxValue);
        var hi = (int)(uint)((coefficient >> 64) & uint.MaxValue);
        return new decimal(lo, mid, hi, negative, (byte)scale);
    }

    private static string Join(IEnumerable<int> scalars)
    {
        var text = new StringBuilder();
        foreach (var scalar in scalars)
        {
            // A lone surrogate is a char of its own, which ConvertFromUtf32 refuses.
            if (scalar < 0x10000)
            {
                text.Append((char)scalar);
            }
            else
            {
                text.Append(char.ConvertFromUtf32(scalar));
            }
        }

        return text.ToString();
    }

    private static List<int> Scalars(string text)
    {
        var scalars = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogatePair(text, i))
            {
                scalars.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else
            {
                scalars.Add(text[i]);
            }
        }

        return scalars;
    }

    private sealed class ByteContent : IEqualityComparer<byte[]>
    {
        public static readonly ByteContent Instance = new();

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj)
        {
            var hash = default(HashCode);
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
