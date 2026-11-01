using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>
/// Seeded samples for the property tests. Every test creates its own <see cref="Random"/> from <see cref="Seed"/>, so a run is
/// reproducible and tests share nothing.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes the samples reproducible; nothing depends on its unpredictability.")]
internal static class KeySamples
{
    public const int Seed = 20261031;
    public const int Samples = 10_000;

    /// <summary>Compares two encoded keys the way DynamoDB compares strings: by their UTF-8 bytes.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>Negative, zero or positive.</returns>
    public static int Compare(byte[] left, byte[] right) => left.AsSpan().SequenceCompareTo(right);

    /// <summary>Finds the first neighbouring pair that is not in strictly ascending byte order.</summary>
    /// <param name="encoded">The keys, in the order of the values they encode.</param>
    /// <returns>A description of the first violation, or <c>none</c>.</returns>
    public static string FirstOrderViolation(IReadOnlyList<byte[]> encoded)
    {
        for (var i = 1; i < encoded.Count; i++)
        {
            if (Compare(encoded[i - 1], encoded[i]) >= 0)
            {
                return $"{i}: {Encoding.UTF8.GetString(encoded[i - 1])} is not below {Encoding.UTF8.GetString(encoded[i])}";
            }
        }

        return "none";
    }

    /// <summary>The power of ten as an unsigned 128-bit integer.</summary>
    /// <param name="exponent">The exponent, at most 38.</param>
    /// <returns>The power.</returns>
    public static UInt128 PowerOfTen(int exponent)
    {
        var value = UInt128.One;
        for (var i = 0; i < exponent; i++)
        {
            value *= 10;
        }

        return value;
    }

    /// <summary>A decimal with the given mantissa and scale.</summary>
    /// <param name="mantissa">The mantissa, below 2^96.</param>
    /// <param name="negative">The sign.</param>
    /// <param name="scale">The number of decimal places, 0 to 28.</param>
    /// <returns>The decimal.</returns>
    public static decimal Decimal(UInt128 mantissa, bool negative, int scale) => new(
        (int)(uint)(mantissa & uint.MaxValue),
        (int)(uint)((mantissa >> 32) & uint.MaxValue),
        (int)(uint)((mantissa >> 64) & uint.MaxValue),
        negative,
        (byte)scale);

    /// <summary>The values at the edges of a fixed-point format, both signs.</summary>
    /// <param name="integerDigits">The number of integer digits.</param>
    /// <param name="fractionDigits">The number of fraction digits.</param>
    /// <returns>The values.</returns>
    public static IEnumerable<decimal> FixedPointBoundaries(int integerDigits, int fractionDigits)
    {
        var digits = integerDigits + fractionDigits;
        var max = PowerOfTen(digits) - 1;
        UInt128[] mantissas =
        [
            0, 1, 2, 9, 10, 11, PowerOfTen(fractionDigits), PowerOfTen(fractionDigits) - 1, PowerOfTen(fractionDigits) + 1,
            max, max - 1, max - 2, PowerOfTen(digits - 1), PowerOfTen(digits - 1) - 1, PowerOfTen(digits - 1) + 1, max / 2,
        ];
        foreach (var mantissa in mantissas.Where(m => m <= max))
        {
            yield return Decimal(mantissa, negative: false, fractionDigits);
            yield return Decimal(mantissa, negative: true, fractionDigits);
        }
    }

    /// <summary>A random value of a fixed-point format; magnitudes of every length are as likely as each other.</summary>
    /// <param name="random">The source.</param>
    /// <param name="integerDigits">The number of integer digits.</param>
    /// <param name="fractionDigits">The number of fraction digits.</param>
    /// <returns>The value, with the scale of the format.</returns>
    public static decimal FixedPoint(Random random, int integerDigits, int fractionDigits)
    {
        var length = random.Next(0, integerDigits + fractionDigits + 1);
        var digits = new StringBuilder("0");
        for (var i = 0; i < length; i++)
        {
            digits.Append((char)('0' + random.Next(10)));
        }

        return Decimal(UInt128.Parse(digits.ToString(), CultureInfo.InvariantCulture), random.Next(2) == 0, fractionDigits);
    }

    /// <summary>A random instant with a random offset. Many are at or next to a round boundary, where truncation matters.</summary>
    /// <param name="random">The source.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset Instant(Random random)
    {
        var ticks = random.NextInt64(0, DateTime.MaxValue.Ticks + 1);
        var unit = random.Next(8) switch
        {
            0 => TimeSpan.TicksPerSecond,
            1 => TimeSpan.TicksPerMinute,
            2 => TimeSpan.TicksPerHour,
            3 => TimeSpan.TicksPerDay,
            4 => TimeSpan.TicksPerMillisecond,
            5 => 10,
            _ => 1,
        };
        ticks = Math.Clamp((ticks - (ticks % unit)) + random.Next(-1, 2), 0, DateTime.MaxValue.Ticks);
        return WithOffset(ticks, TimeSpan.FromMinutes(random.Next(-840, 841)));
    }

    /// <summary>An instant of the given UTC ticks, shown with an offset if the local time is a valid time too.</summary>
    /// <param name="utcTicks">The ticks of the instant in UTC.</param>
    /// <param name="offset">The offset to try.</param>
    /// <returns>The instant.</returns>
    public static DateTimeOffset WithOffset(long utcTicks, TimeSpan offset)
    {
        var local = utcTicks + offset.Ticks;
        return local >= 0 && local <= DateTime.MaxValue.Ticks
            ? new DateTimeOffset(new DateTime(local, DateTimeKind.Unspecified), offset)
            : new DateTimeOffset(utcTicks, TimeSpan.Zero);
    }

    /// <summary>The start of the bucket that a timestamp format puts an instant into, computed from the date fields, not from text.</summary>
    /// <param name="utcTicks">The instant.</param>
    /// <param name="format">The format.</param>
    /// <returns>The ticks of the start of the bucket.</returns>
    public static long Bucket(long utcTicks, KeyTimeFormat format)
    {
        var time = new DateTime(utcTicks, DateTimeKind.Utc);
        return format switch
        {
            KeyTimeFormat.Year => new DateTime(time.Year, 1, 1).Ticks,
            KeyTimeFormat.Month => new DateTime(time.Year, time.Month, 1).Ticks,
            KeyTimeFormat.Day => time.Date.Ticks,
            KeyTimeFormat.Hour => new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0).Ticks,
            KeyTimeFormat.Minute => new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0).Ticks,
            KeyTimeFormat.Second => utcTicks - (utcTicks % TimeSpan.TicksPerSecond),
            KeyTimeFormat.Millisecond => utcTicks - (utcTicks % TimeSpan.TicksPerMillisecond),
            KeyTimeFormat.Microsecond => utcTicks - (utcTicks % 10),
            _ => utcTicks,
        };
    }

    /// <summary>Instants at the edges of the range and around the dates that calendars get wrong.</summary>
    /// <returns>The instants.</returns>
    public static IEnumerable<DateTimeOffset> InstantBoundaries()
    {
        long[] ticks =
        [
            0, 1, 9_999_999, TimeSpan.TicksPerSecond, DateTime.MaxValue.Ticks, DateTime.MaxValue.Ticks - 1,
            new DateTime(1999, 12, 31, 23, 59, 59).Ticks + 9_999_999, new DateTime(2000, 1, 1).Ticks,
            new DateTime(2024, 2, 29, 23, 59, 59).Ticks + 9_999_999, new DateTime(2024, 3, 1).Ticks,
            new DateTime(1000, 1, 1).Ticks - 1, new DateTime(1000, 1, 1).Ticks, new DateTime(2026, 10, 31, 12, 34, 56).Ticks + 7_890_123,
        ];
        return ticks.Select(t => new DateTimeOffset(t, TimeSpan.Zero));
    }

    /// <summary>A random non-empty string of characters from <paramref name="alphabet"/>, whole code points.</summary>
    /// <param name="random">The source.</param>
    /// <param name="alphabet">The code points to choose from.</param>
    /// <param name="maxLength">The maximum number of code points.</param>
    /// <returns>The string.</returns>
    public static string Text(Random random, IReadOnlyList<int> alphabet, int maxLength)
    {
        var builder = new StringBuilder();
        for (var i = random.Next(1, maxLength + 1); i > 0; i--)
        {
            builder.Append(char.ConvertFromUtf32(alphabet[random.Next(alphabet.Count)]));
        }

        return builder.ToString();
    }
}
