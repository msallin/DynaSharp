using System.Diagnostics.CodeAnalysis;
using System.Text;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>Properties of the text-like encodings (string, Guid, custom type) over seeded random values.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes the samples reproducible; nothing depends on its unpredictability.")]
public sealed class KeyTextPropertyTests
{
    private const string Name = "Value";

    // ASCII without control characters and without '#', Latin-1, the edge of the BMP, a CJK block, and supplementary planes. The pairs
    // U+FFFF and U+10000, and U+E000 and U+D7FF, are where UTF-16 order and UTF-8 order part ways.
    private static readonly int[] Alphabet =
    [
        .. Enumerable.Range(0x20, 0x7F - 0x20).Where(c => c != '#'),
        0x7F, 0x80, 0xA0, 0xE9, 0xFC, 0x7FF, 0x800, 0x20AC, 0xD7FF, 0xE000, 0xFFFD, 0xFFFF,
        0x65E5, 0x672C, 0x8A9E, 0x10000, 0x1F600, 0x10FFFE,
    ];

    [Test]
    public async Task WriteString_StringsInCodePointOrder_GiveBytesInOrder()
    {
        // Arrange
        var strings = RandomStrings().Distinct().OrderBy(s => s, CodePointOrder.Instance).ToList();

        // Act
        var encoded = strings.Select(s => KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteString(s, "#"u8, Name), 8)).ToList();

        // Assert
        await Assert.That(KeySamples.FirstOrderViolation(encoded)).IsEqualTo("none");
    }

    [Test]
    public async Task WriteString_ValueTheOrderOfUtf16Gets_WrongIsOrderedByCodePoint()
    {
        // Arrange (U+FFFF is below U+10000; their UTF-16 forms, D800 DC00 and FFFF, compare the other way round)
        var low = "￿";
        var high = char.ConvertFromUtf32(0x10000);

        // Act
        var utf16Order = string.CompareOrdinal(low, high);
        var keyOrder = KeySamples.Compare(
            KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteString(low, "#"u8, Name), 8),
            KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteString(high, "#"u8, Name), 8));

        // Assert
        await Assert.That((utf16Order > 0, keyOrder < 0)).IsEqualTo((true, true));
    }

    [Test]
    public async Task WriteString_AnyValidString_IsItsUtf8BytesAndReadsBack()
    {
        // Arrange
        var strings = RandomStrings().ToList();

        // Act
        var failures = strings.Count(s => !StringRoundTrips(s));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteGuid_RandomGuids_ReadBackAsTheSameGuidInLowercaseWithHyphens()
    {
        // Arrange
        var random = new Random(KeySamples.Seed);
        var guids = new List<Guid> { Guid.Empty, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            guids.Add(RandomGuid(random));
        }

        // Act
        var failures = guids.Count(g => !GuidRoundTrips(g));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteGuid_Version7GuidsOfLaterTimes_GiveLaterBytes()
    {
        // Arrange
        var random = new Random(KeySamples.Seed);
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var guids = new List<Guid>();
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            time = time.AddMilliseconds(random.Next(1, 100_000));
            guids.Add(Guid.CreateVersion7(time));
        }

        // Act
        var encoded = guids.Select(g => KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteGuid(g), 8)).ToList();

        // Assert
        await Assert.That(KeySamples.FirstOrderViolation(encoded)).IsEqualTo("none");
    }

    [Test]
    public async Task WriteFormattable_RandomIntegers_ReadBackAsTheSameValue()
    {
        // Arrange
        var random = new Random(KeySamples.Seed);
        var values = Enumerable.Range(0, KeySamples.Samples).Select(_ => random.Next(int.MinValue, int.MaxValue)).Concat([0, int.MinValue, int.MaxValue]);

        // Act
        var failures = values.Count(v => !FormattableRoundTrips(v));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task WriteUtf8Value_AnyValidString_GivesTheSameBytesAsWriteString()
    {
        // Arrange
        var strings = RandomStrings().ToList();

        // Act
        var failures = strings.Count(s => !KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteString(s, "#"u8, Name), 8)
            .AsSpan().SequenceEqual(KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteUtf8Value(Encoding.UTF8.GetBytes(s), "#"u8, Name), 8)));

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    private static List<string> RandomStrings()
    {
        var random = new Random(KeySamples.Seed);
        var strings = new List<string>
        {
            "a", "A", "z", "~", " ", "\u007F", "\u0080", "￿", char.ConvertFromUtf32(0x10000), char.ConvertFromUtf32(0x10FFFE), "퟿", "",
            "ab", "a ", " a", "a!", "a\"", "a$", "aa", "aB", "Ab", "￿a", char.ConvertFromUtf32(0x10000) + "a",
        };
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            strings.Add(KeySamples.Text(random, Alphabet, 12));
        }

        return strings;
    }

    private static Guid RandomGuid(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static bool StringRoundTrips(string value)
    {
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteString(value, "#"u8, Name), 8);
        var reader = new KeyReader(bytes);
        return bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(value))
            && reader.TryReadString("#"u8, out var read)
            && reader.IsAtEnd
            && read == value;
    }

    private static bool GuidRoundTrips(Guid value)
    {
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteGuid(value), 8);
        var reader = new KeyReader(bytes);
        var text = Encoding.ASCII.GetString(bytes);
        return text == value.ToString("D") && text.All(c => !char.IsAsciiLetterUpper(c)) && reader.TryReadGuid(out var read) && reader.IsAtEnd && read == value;
    }

    private static bool FormattableRoundTrips(int value)
    {
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteFormattable(value, "#"u8, Name), 8);
        var reader = new KeyReader(bytes);
        return reader.TryReadParsable<int>("#"u8, out var read) && reader.IsAtEnd && read == value;
    }

    /// <summary>Orders strings by Unicode code point, which is the order of their UTF-8 bytes.</summary>
    private sealed class CodePointOrder : IComparer<string>
    {
        public static CodePointOrder Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            using var left = x!.EnumerateRunes().GetEnumerator();
            using var right = y!.EnumerateRunes().GetEnumerator();
            while (true)
            {
                var hasLeft = left.MoveNext();
                var hasRight = right.MoveNext();
                if (!hasLeft || !hasRight)
                {
                    return hasLeft.CompareTo(hasRight);
                }

                var difference = left.Current.Value.CompareTo(right.Current.Value);
                if (difference != 0)
                {
                    return difference;
                }
            }
        }
    }
}
