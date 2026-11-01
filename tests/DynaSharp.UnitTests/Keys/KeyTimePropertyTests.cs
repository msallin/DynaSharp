using System.Diagnostics.CodeAnalysis;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>
/// Properties of the timestamp and date encodings over seeded random instants (many next to a truncation boundary) and the
/// instants at the edges of the range: chronological byte order, truncation into buckets, round trips and prefix relations.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes the samples reproducible; nothing depends on its unpredictability.")]
public sealed class KeyTimePropertyTests
{
    private const string Name = "Value";

    public static IEnumerable<KeyTimeFormat> Formats() => Enum.GetValues<KeyTimeFormat>();

    [Test]
    [MethodDataSource(nameof(Formats))]
    public async Task WriteTimestamp_LaterInstants_NeverGiveEarlierBytesAndGiveLaterBytesAcrossBuckets(KeyTimeFormat format)
    {
        // Arrange
        var instants = SortedInstants();

        // Act
        var violations = Violations(instants, format);

        // Assert
        await Assert.That(violations).IsEqualTo("none");
    }

    [Test]
    [MethodDataSource(nameof(Formats))]
    public async Task WriteTimestamp_AnyInstant_ReadsBackAsTheStartOfItsBucket(KeyTimeFormat format)
    {
        // Arrange
        var instants = SortedInstants();

        // Act
        var failures = instants.Count(i => !ReadsBackAsBucket(i, format));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(nameof(Formats))]
    public async Task WriteTimestamp_AnyUtcDateTime_ReadsBackAsTheStartOfItsBucketWithKindUtc(KeyTimeFormat format)
    {
        // Arrange
        var instants = SortedInstants();

        // Act
        var failures = instants.Count(i => !DateTimeReadsBackAsBucket(i, format));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteTimestamp_TicksFormat_IsLosslessForTheInstant()
    {
        // Arrange
        var instants = SortedInstants();

        // Act
        var failures = instants.Count(i => !ReadsBackAsBucket(i, KeyTimeFormat.Ticks) || Bucket(i, KeyTimeFormat.Ticks) != i.UtcTicks);

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteTimestamp_FormatsUpToMinutes_AreBytePrefixesOfTheFinerFormats()
    {
        // Arrange
        var instants = SortedInstants();

        // Act
        var failures = instants.Count(i => !AreChainOfPrefixes(i));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteTimestamp_SecondAndFinerFormats_AreNeverPrefixesOfEachOther()
    {
        // Arrange
        var instants = SortedInstants();

        // Act
        var failures = instants.Count(HasAPrefixAmongFinerFormats);

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteTimestamp_EveryFormat_HasItsFixedWidth()
    {
        // Arrange
        var instants = SortedInstants();
        int[] widths = [4, 7, 10, 13, 16, 20, 24, 27, 28];

        // Act
        var failures = instants.Count(i => Enum.GetValues<KeyTimeFormat>().Any(f => Encode(i, f).Length != widths[(int)f]));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteDate_AscendingDates_GiveAscendingBytesThatReadBack()
    {
        // Arrange
        var random = new Random(KeySamples.Seed);
        var days = new SortedSet<int> { 0, 1, DateOnly.MaxValue.DayNumber, DateOnly.MaxValue.DayNumber - 1, new DateOnly(2024, 2, 29).DayNumber, new DateOnly(1999, 12, 31).DayNumber };
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            days.Add(random.Next(0, DateOnly.MaxValue.DayNumber + 1));
        }

        // Act
        var dates = days.Select(DateOnly.FromDayNumber).ToList();
        var encoded = dates.Select(d => KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteDate(d), 4)).ToList();
        var failures = dates.Zip(encoded).Count(pair => !DateReadsBack(pair.First, pair.Second));

        // Assert
        await Assert.That((KeySamples.FirstOrderViolation(encoded), failures)).IsEqualTo(("none", 0));
    }

    private static List<DateTimeOffset> SortedInstants()
    {
        var random = new Random(KeySamples.Seed);
        var instants = KeySamples.InstantBoundaries().ToList();
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            instants.Add(KeySamples.Instant(random));
        }

        return [.. instants.OrderBy(i => i.UtcTicks)];
    }

    private static string Violations(List<DateTimeOffset> sorted, KeyTimeFormat format)
    {
        for (var i = 1; i < sorted.Count; i++)
        {
            var comparison = KeySamples.Compare(Encode(sorted[i - 1], format), Encode(sorted[i], format));
            var sameBucket = Bucket(sorted[i - 1], format) == Bucket(sorted[i], format);
            if (sameBucket ? comparison != 0 : comparison >= 0)
            {
                return $"{sorted[i - 1].UtcTicks} and {sorted[i].UtcTicks}: comparison {comparison}, same bucket {sameBucket}";
            }
        }

        return "none";
    }

    private static long Bucket(DateTimeOffset instant, KeyTimeFormat format) => KeySamples.Bucket(instant.UtcTicks, format);

    private static byte[] Encode(DateTimeOffset instant, KeyTimeFormat format) =>
        KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteTimestamp(instant, format), 8);

    private static bool ReadsBackAsBucket(DateTimeOffset instant, KeyTimeFormat format)
    {
        var reader = new KeyReader(Encode(instant, format));
        return reader.TryReadTimestamp(format, out DateTimeOffset read)
            && reader.IsAtEnd
            && read.Offset == TimeSpan.Zero
            && read.UtcTicks == Bucket(instant, format);
    }

    private static bool DateTimeReadsBackAsBucket(DateTimeOffset instant, KeyTimeFormat format)
    {
        var utc = instant.UtcDateTime;
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteTimestamp(utc, format, Name), 8);
        var reader = new KeyReader(bytes);
        return bytes.AsSpan().SequenceEqual(Encode(instant, format))
            && reader.TryReadTimestamp(format, out DateTime read)
            && reader.IsAtEnd
            && read.Kind == DateTimeKind.Utc
            && read.Ticks == Bucket(instant, format);
    }

    private static bool AreChainOfPrefixes(DateTimeOffset instant)
    {
        var coarse = new[] { KeyTimeFormat.Year, KeyTimeFormat.Month, KeyTimeFormat.Day, KeyTimeFormat.Hour, KeyTimeFormat.Minute };
        var fine = new[] { KeyTimeFormat.Second, KeyTimeFormat.Millisecond, KeyTimeFormat.Microsecond, KeyTimeFormat.Ticks };
        return coarse.All(c => fine.All(f => Encode(instant, f).AsSpan().StartsWith(Encode(instant, c))))
            && coarse.Zip(coarse.Skip(1)).All(pair => Encode(instant, pair.Second).AsSpan().StartsWith(Encode(instant, pair.First)));
    }

    private static bool HasAPrefixAmongFinerFormats(DateTimeOffset instant)
    {
        var formats = new[] { KeyTimeFormat.Second, KeyTimeFormat.Millisecond, KeyTimeFormat.Microsecond, KeyTimeFormat.Ticks };
        return formats.Any(a => formats.Any(b => a != b && Encode(instant, b).AsSpan().StartsWith(Encode(instant, a))));
    }

    private static bool DateReadsBack(DateOnly date, byte[] bytes)
    {
        var reader = new KeyReader(bytes);
        return reader.TryReadDate(out var read) && reader.IsAtEnd && read == date;
    }
}
