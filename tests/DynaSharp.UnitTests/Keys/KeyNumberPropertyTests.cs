using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>
/// Properties of the number encodings over seeded random values and the values at the edges of every format: ordinal byte order
/// equals numeric order for the fixed formats, and what is written reads back.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes the samples reproducible; nothing depends on its unpredictability.")]
public sealed class KeyNumberPropertyTests
{
    private const string Name = "Value";

    public static IEnumerable<(int IntegerDigits, int FractionDigits)> FixedPointFormats() =>
    [
        (4, 2), (1, 0), (10, 0), (10, 2), (18, 10), (28, 0), (1, 27), (14, 14), (20, 0), (3, 5), (2, 1),
    ];

    public static IEnumerable<int> DigitWidths() => [1, 2, 5, 10, 19, 20, 25];

    [Test]
    [MethodDataSource(nameof(FixedPointFormats))]
    public async Task WriteFixedPoint_AscendingValues_GiveAscendingBytes((int IntegerDigits, int FractionDigits) format)
    {
        // Arrange
        var values = FixedPointValues(format);

        // Act
        var encoded = values.Select(v => Encode(v, format)).ToList();

        // Assert
        await Assert.That(KeySamples.FirstOrderViolation(encoded)).IsEqualTo("none");
    }

    [Test]
    [MethodDataSource(nameof(FixedPointFormats))]
    public async Task WriteFixedPoint_AnyValue_ReadsBackAsTheSameValueInTheFixedWidth((int IntegerDigits, int FractionDigits) format)
    {
        // Arrange
        var values = FixedPointValues(format);
        var width = 1 + format.IntegerDigits + (format.FractionDigits == 0 ? 0 : 1 + format.FractionDigits);

        // Act
        var failures = values.Count(v => !RoundTrips(v, format, width));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(nameof(DigitWidths))]
    public async Task WriteDigits_AscendingValues_GiveAscendingBytes(int width)
    {
        // Arrange
        var values = DigitValues(width);

        // Act
        var encoded = values.Select(v => KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteDigits(v, width, Name), 8)).ToList();

        // Assert
        await Assert.That(KeySamples.FirstOrderViolation(encoded)).IsEqualTo("none");
    }

    [Test]
    [MethodDataSource(nameof(DigitWidths))]
    public async Task WriteDigits_AnyValue_ReadsBackAsTheSameValueInTheFixedWidth(int width)
    {
        // Arrange
        var values = DigitValues(width);

        // Act
        var failures = values.Count(v => !DigitsRoundTrip(v, width));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteInteger_RandomLongs_ReadBackAsTheSameValue()
    {
        // Arrange
        var random = new Random(KeySamples.Seed);
        long[] boundaries = [0, 1, -1, 9, 10, -10, 99, 100, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1];
        var values = boundaries.Concat(Enumerable.Range(0, KeySamples.Samples).Select(_ => random.NextInt64(long.MinValue, long.MaxValue) >> random.Next(64)));

        // Act
        var failures = values.Count(v => !LongRoundTrips(v));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteInteger_RandomUnsignedLongs_ReadBackAsTheSameValue()
    {
        // Arrange
        var random = new Random(KeySamples.Seed);
        ulong[] boundaries = [0, 1, 9, 10, 99, 100, long.MaxValue, (ulong)long.MaxValue + 1, ulong.MaxValue, ulong.MaxValue - 1];
        var values = boundaries.Concat(Enumerable.Range(0, KeySamples.Samples).Select(_ => NextUInt64(random) >> random.Next(64)));

        // Act
        var failures = values.Count(v => !UnsignedRoundTrips(v));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteDecimal_RandomValues_ReadBackAsTheSameNumberInItsShortestForm()
    {
        // Arrange
        var values = PlainDecimals();

        // Act
        var failures = values.Count(v => !PlainRoundTrips(v));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteDecimal_EqualNumbersWithDifferentScales_GiveTheSameKey()
    {
        // Arrange
        var random = new Random(KeySamples.Seed);
        var pairs = Enumerable.Range(0, KeySamples.Samples).Select(_ =>
        {
            var mantissa = (UInt128)random.NextInt64(0, 10_000_000_000);
            var scale = random.Next(0, 14);
            var extra = random.Next(1, 15);
            var negative = random.Next(2) == 0;
            return (Short: KeySamples.Decimal(mantissa, negative, scale), Long: KeySamples.Decimal(mantissa * KeySamples.PowerOfTen(extra), negative, scale + extra));
        });

        // Act
        var failures = pairs.Count(p => !PlainEqual(p.Short, p.Long));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteFixedPoint_ValuesThatDiffer_NeverShareAKey()
    {
        // Arrange
        var format = (IntegerDigits: 2, FractionDigits: 1);
        var values = Enumerable.Range(-999, 1999).Select(i => KeySamples.Decimal((UInt128)Math.Abs(i), i < 0, 1)).ToList();

        // Act
        var distinctKeys = values.Select(v => Convert.ToHexString(Encode(v, format))).Distinct().Count();

        // Assert
        await Assert.That(distinctKeys).IsEqualTo(1999);
    }

    private static List<decimal> FixedPointValues((int IntegerDigits, int FractionDigits) format)
    {
        var random = new Random(KeySamples.Seed);
        var values = new SortedSet<decimal>(KeySamples.FixedPointBoundaries(format.IntegerDigits, format.FractionDigits));
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            values.Add(KeySamples.FixedPoint(random, format.IntegerDigits, format.FractionDigits));
        }

        return [.. values];
    }

    private static List<ulong> DigitValues(int width)
    {
        var random = new Random(KeySamples.Seed);
        var digits = Math.Min(width, 20);
        var limit = digits == 20 ? ulong.MaxValue : (ulong)KeySamples.PowerOfTen(digits) - 1;
        var values = new SortedSet<ulong> { 0, 1, 9, 10, limit, limit - 1, limit / 2 };
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            var length = random.Next(1, digits + 1);
            var cap = length == 20 ? ulong.MaxValue : (ulong)KeySamples.PowerOfTen(length) - 1;
            values.Add(cap == ulong.MaxValue ? NextUInt64(random) : NextUInt64(random) % (cap + 1));
        }

        values.RemoveWhere(v => v > limit);
        return [.. values];
    }

    private static ulong NextUInt64(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static List<decimal> PlainDecimals()
    {
        var random = new Random(KeySamples.Seed);
        var values = new List<decimal> { 0m, -0m, 1m, -1m, 0.1m, -0.1m, 100m, 1.10m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m, -0.0000000000000000000000000001m, 7.0m, 1.000m };
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            var digits = random.Next(1, 29);
            var text = new StringBuilder();
            for (var d = 0; d < digits; d++)
            {
                text.Append((char)('0' + random.Next(10)));
            }

            values.Add(KeySamples.Decimal(UInt128.Parse(text.ToString(), CultureInfo.InvariantCulture), random.Next(2) == 0, random.Next(0, 29)));
        }

        return values;
    }

    private static byte[] Encode(decimal value, (int IntegerDigits, int FractionDigits) format) =>
        KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteFixedPoint(value, format.IntegerDigits, format.FractionDigits, Name), 8);

    private static bool RoundTrips(decimal value, (int IntegerDigits, int FractionDigits) format, int width)
    {
        var bytes = Encode(value, format);
        var reader = new KeyReader(bytes);
        return bytes.Length == width
            && reader.TryReadFixedPoint(format.IntegerDigits, format.FractionDigits, out var read)
            && reader.IsAtEnd
            && read == value;
    }

    private static bool DigitsRoundTrip(ulong value, int width)
    {
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteDigits(value, width, Name), 8);
        var reader = new KeyReader(bytes);
        return bytes.Length == width && reader.TryReadDigits(width, out ulong read) && reader.IsAtEnd && read == value;
    }

    private static bool LongRoundTrips(long value)
    {
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteInteger(value, default, Name), 8);
        var reader = new KeyReader(bytes);
        return Encoding.ASCII.GetString(bytes) == value.ToString(CultureInfo.InvariantCulture) && reader.TryReadInt64(default, out var read) && reader.IsAtEnd && read == value;
    }

    private static bool UnsignedRoundTrips(ulong value)
    {
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteInteger(value, default, Name), 8);
        var reader = new KeyReader(bytes);
        return Encoding.ASCII.GetString(bytes) == value.ToString(CultureInfo.InvariantCulture) && reader.TryReadUInt64(default, out var read) && reader.IsAtEnd && read == value;
    }

    private static bool PlainRoundTrips(decimal value)
    {
        var bytes = PlainBytes(value);
        var reader = new KeyReader(bytes);
        return reader.TryReadDecimal(default, out var read) && reader.IsAtEnd && read == value && PlainBytes(read).AsSpan().SequenceEqual(bytes);
    }

    private static bool PlainEqual(decimal left, decimal right) => PlainBytes(left).AsSpan().SequenceEqual(PlainBytes(right));

    private static byte[] PlainBytes(decimal value) => KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteDecimal(value, default, Name), 8);
}
