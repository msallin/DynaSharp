using System.Net;
using System.Text;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

public sealed class KeyReaderTests
{
    private delegate bool KeyReaderFunc(ref KeyReader reader);

    public static IEnumerable<(int IntegerDigits, int FractionDigits)> InvalidFixedPointFormats() =>
    [
        (0, 2), (-1, 2), (4, -1), (20, 9), (29, 0), (1, 28), (int.MaxValue, 1), (1, int.MaxValue),
    ];

    public static IEnumerable<int> UndefinedTimeFormats() => [-1, 9, int.MaxValue];

    [Test]
    public async Task IsAtEnd_EmptyText_IsTrue()
    {
        // Arrange
        var atEnd = ReadEmpty();

        // Act
        var result = atEnd;

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadLiteral_EmptyLiteral_SucceedsWithoutConsuming()
    {
        // Arrange
        // Act
        var result = ReadAll("abc"u8, static (ref KeyReader reader) => reader.TryReadLiteral(default) && reader.TryReadLiteral("abc"u8) && reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadLiteral_OtherText_FailsAndLeavesTheReaderWhereItWas()
    {
        // Arrange
        // Act
        var result = ReadAll("USER#1"u8, static (ref KeyReader reader) => !reader.TryReadLiteral("ORDER#"u8) && reader.TryReadLiteral("USER#"u8) && !reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadString_Failure_LeavesTheReaderWhereItWasAndReturnsNull()
    {
        // Arrange
        // Act
        var result = ReadAll(
            "a\u0001b"u8,
            static (ref KeyReader reader) => !reader.TryReadString(default, out var value) && value is null && reader.TryReadLiteral("a"u8));

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadString_StopsAtTheFirstDelimiterOfTheSet()
    {
        // Arrange
        // Act
        var result = ReadAll(
            "a b-c#d"u8,
            static (ref KeyReader reader) => reader.TryReadString("#-"u8, out var value) && value == "a b" && reader.TryReadLiteral("-c#d"u8) && reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadUtf8Value_ValidSegment_ReturnsASliceOfTheInput()
    {
        // Arrange
        // Act
        var result = ReadAll(
            "Paid#1"u8,
            static (ref KeyReader reader) => reader.TryReadUtf8Value("#"u8, out var value) && value.SequenceEqual("Paid"u8) && reader.TryReadLiteral("#1"u8));

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadUtf8Value_Failure_ReturnsEmptyAndLeavesTheReader()
    {
        // Arrange
        // Act
        var result = ReadAll(
            "#1"u8,
            static (ref KeyReader reader) => !reader.TryReadUtf8Value("#"u8, out var value) && value.IsEmpty && reader.TryReadLiteral("#1"u8));

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadGuid_ShortInput_FailsAndLeavesTheReader()
    {
        // Arrange
        // Act
        var result = ReadAll("4f9c2a7e"u8, static (ref KeyReader reader) => !reader.TryReadGuid(out var value) && value == Guid.Empty && !reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task TryReadDigits_WidthBelowOne_Fails(int width)
    {
        // Arrange
        // Act
        var result = ReadAll("123"u8, (ref KeyReader reader) => reader.TryReadDigits(width, out ulong _) | reader.TryReadDigits(width, out long _));

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryReadDigits_SignedNumberAboveLongMax_FailsAndLeavesTheReader()
    {
        // Arrange
        // Act
        var result = ReadAll(
            "9223372036854775808"u8,
            static (ref KeyReader reader) => !reader.TryReadDigits(19, out long value) && value == 0 && reader.TryReadDigits(19, out ulong unsigned) && unsigned == 9223372036854775808UL);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadFixedPoint_Failure_ReturnsZeroAndLeavesTheReader()
    {
        // Arrange
        // Act
        var result = ReadAll("N9999.99"u8, static (ref KeyReader reader) => !reader.TryReadFixedPoint(4, 2, out var value) && value == 0m && !reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadFixedPoint_Value_HasTheScaleOfTheFormat()
    {
        // Arrange
        // Act
        var scale = ReadScale("P0012.50"u8);

        // Assert
        await Assert.That(scale).IsEqualTo(2);
    }

    [Test]
    [MethodDataSource(nameof(InvalidFixedPointFormats))]
    public async Task TryReadFixedPoint_InvalidFormat_ThrowsArgumentOutOfRangeException((int IntegerDigits, int FractionDigits) format)
    {
        // Arrange
        // Act
        var act = () => ReadAll("P0"u8, (ref KeyReader reader) => reader.TryReadFixedPoint(format.IntegerDigits, format.FractionDigits, out _));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [MethodDataSource(nameof(UndefinedTimeFormats))]
    public async Task TryReadTimestamp_UndefinedFormat_ThrowsArgumentOutOfRangeException(int format)
    {
        // Arrange
        // Act
        var forOffset = () => ReadAll("2026"u8, (ref KeyReader reader) => reader.TryReadTimestamp((KeyTimeFormat)format, out DateTimeOffset _));
        var forDateTime = () => ReadAll("2026"u8, (ref KeyReader reader) => reader.TryReadTimestamp((KeyTimeFormat)format, out DateTime _));

        // Assert
        await Assert.That(forOffset).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(forDateTime).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TryReadTimestamp_Failure_ReturnsDefaultsAndLeavesTheReader()
    {
        // Arrange
        // Act
        var result = ReadAll(
            "2026-02-30"u8,
            static (ref KeyReader reader) =>
                !reader.TryReadTimestamp(KeyTimeFormat.Day, out DateTimeOffset offset) && offset == default
                && !reader.TryReadTimestamp(KeyTimeFormat.Day, out DateTime time) && time == default
                && !reader.TryReadDate(out var date) && date == default
                && !reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadTimestamp_DateTime_HasKindUtc()
    {
        // Arrange
        // Act
        var kind = ReadKind("2026-10-31T12:34:56.789Z"u8);

        // Assert
        await Assert.That(kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Test]
    public async Task TryReadParsable_SegmentOfMoreThanTheStackLimit_ReadsItFromTheHeap()
    {
        // Arrange
        var text = new string('x', 1000);

        // Act
        var result = ReadAll(Encoding.UTF8.GetBytes(text), (ref KeyReader reader) => reader.TryReadParsable<RawText>(default, out var value) && value.Text == text && reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadParsable_TypeThatDoesNotFormatBackWhatItParsed_Fails()
    {
        // Arrange
        // Act
        var result = ReadAll("21"u8, static (ref KeyReader reader) => !reader.TryReadParsable<Doubling>(default, out _) && !reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadParsable_TypeThatFormatsLongerThanTheText_Fails()
    {
        // Arrange (1.1 parses as 1.0.0.1, which is longer)
        // Act
        var result = ReadAll("1.1"u8, static (ref KeyReader reader) => !reader.TryReadParsable<IPAddress>(default, out var address) && address is null && !reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadParsable_TypeThatFormatsToTheSameLengthWithOtherBytes_Fails()
    {
        // Arrange (the text is a parse of its own value, but with a different spelling)
        // Act
        var result = ReadAll("a1"u8, static (ref KeyReader reader) => !reader.TryReadParsable<Shouting>(default, out _) && !reader.IsAtEnd);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryReadParsable_EmptyText_FailsWithoutCallingTheType()
    {
        // Arrange
        // Act
        var result = ReadAll(default, static (ref KeyReader reader) => !reader.TryReadParsable<RawText>(default, out _));

        // Assert
        await Assert.That(result).IsTrue();
    }

    private static bool ReadAll(ReadOnlySpan<byte> input, KeyReaderFunc read)
    {
        var reader = new KeyReader(input);
        return read(ref reader);
    }

    private static bool ReadEmpty() => new KeyReader(default).IsAtEnd;

    private static int ReadScale(ReadOnlySpan<byte> input)
    {
        var reader = new KeyReader(input);
        _ = reader.TryReadFixedPoint(4, 2, out var value);
        return (decimal.GetBits(value)[3] >> 16) & 0xFF;
    }

    private static DateTimeKind ReadKind(ReadOnlySpan<byte> input)
    {
        var reader = new KeyReader(input);
        _ = reader.TryReadTimestamp(KeyTimeFormat.Millisecond, out DateTime value);
        return value.Kind;
    }
}
