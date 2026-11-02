using System.Text;
using DynaSharp.Keys;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Keys;

public sealed class KeyWriterTests
{
    private const string Name = "Name";

    public static IEnumerable<KeyVector> AcceptedWrites() => KeyVectors.Writes(error: false);

    public static IEnumerable<KeyVector> RejectedWrites() => KeyVectors.Writes(error: true);

    public static IEnumerable<FailingWrite> FailingWrites() =>
    [
        .. KeyFormatExceptionTests.FailingWrites(),
        new("empty string", string.Empty, static (ref KeyWriter writer) => writer.WriteString(string.Empty, default, Name)),
        new("null", string.Empty, static (ref KeyWriter writer) => writer.WriteString(null!, default, Name)),
        new("no name of an enum", string.Empty, static (ref KeyWriter writer) => writer.WriteEnumName(default, default, Name)),
        new("formattable that never fits", string.Empty, static (ref KeyWriter writer) => writer.WriteFormattable(default(NeverFits), default, Name)),
    ];

    public static IEnumerable<(int IntegerDigits, int FractionDigits)> InvalidFixedPointFormats() =>
    [
        (0, 2), (-1, 2), (4, -1), (20, 9), (29, 0), (1, 28), (int.MaxValue, 1), (1, int.MaxValue),
    ];

    public static IEnumerable<int> UndefinedTimeFormats() => [-1, 9, int.MaxValue];

    [Test]
    public async Task WrittenSpan_NewWriter_IsEmpty()
    {
        // Arrange
        var bytes = Write(static (ref KeyWriter _) => { });

        // Act
        var length = bytes.Length;

        // Assert
        await Assert.That(length).IsEqualTo(0);
    }

    [Test]
    public async Task WriteLiteral_Literal_AppendsTheBytesAsTheyAre()
    {
        // Arrange
        // Act
        var bytes = Write(static (ref KeyWriter writer) =>
        {
            writer.WriteLiteral("ORDER#"u8);
            writer.WriteLiteral(default);
            writer.WriteLiteral("x"u8);
        });

        // Assert
        await Assert.That(Encoding.UTF8.GetString(bytes)).IsEqualTo("ORDER#x");
    }

    [Test]
    [Arguments("USER#1", true, "USER#1")]
    [Arguments("Zürich\U0001F600", true, "Zürich\U0001F600")]
    [Arguments("", true, "")]
    [Arguments(null, false, "x")]
    [Arguments("a\uD800", false, "x")]
    [Arguments("\uDC00b", false, "x")]
    public async Task TryWriteText_Text_AppendsWhatIsWellFormedAndNothingElse(string? text, bool expected, string written)
    {
        // Arrange (no room, so that the text goes through the pool as well; the writer holds an "x" that must stay)
        var isWritten = false;

        // Act
        var bytes = Write(
            (ref KeyWriter writer) =>
            {
                writer.WriteLiteral("x"u8);
                isWritten = writer.TryWriteText(text);
            },
            bufferLength: 1);

        // Assert
        await Assert.That((isWritten, Encoding.UTF8.GetString(bytes))).IsEqualTo((expected, expected ? "x" + written : written));
    }

    [Test]
    public async Task WriteUpperBound_AfterAPrefix_AppendsTheHighestScalarAsFourBytes()
    {
        // Arrange
        // Act
        var bytes = Write(static (ref KeyWriter writer) =>
        {
            writer.WriteLiteral("USER#1#"u8);
            writer.WriteUpperBound();
        });

        // Assert
        await Assert.That(Convert.ToHexString(bytes)).IsEqualTo(Convert.ToHexString("USER#1#"u8) + "F48FBFBF");
    }

    [Test]
    public async Task ToString_AfterWrites_ReturnsTheKeyAsText()
    {
        // Arrange
        // Act
        var text = Text(static (ref KeyWriter writer) =>
        {
            writer.WriteLiteral("CITY#"u8);
            writer.WriteString("Zürich", "#"u8, Name);
        });

        // Assert
        await Assert.That(text).IsEqualTo("CITY#Zürich");
    }

    [Test]
    [MethodDataSource(nameof(AcceptedWrites))]
    public async Task Write_VectorWithPrefilledBufferAndDirtyPool_ExposesOnlyWhatWasWritten(KeyVector vector)
    {
        // Arrange (the stack buffer holds 0xFF, every rented array 0xAB; the oracle's bytes contain neither pattern by accident)
        var expected = Convert.ToHexString(vector.GetExpectedBytes());
        var pool = new DirtyArrayPool();

        // Act
        var tight = KeyVectorRunner.Write(vector, 3, pool);
        var roomy = KeyVectorRunner.Write(vector, 512, pool);

        // Assert
        await Assert.That((Convert.ToHexString(tight), Convert.ToHexString(roomy))).IsEqualTo((expected, expected));
    }

    [Test]
    public async Task Write_RejectedValue_LeavesWhatWasWrittenUnchangedAndWritingContinues()
    {
        // Arrange
        // Act
        var text = Text(static (ref KeyWriter writer) =>
        {
            writer.WriteLiteral("A#"u8);
            TryWrite(ref writer, "a#b");
            writer.WriteString("ok", "#"u8, Name);
        });

        // Assert
        await Assert.That(text).IsEqualTo("A#ok");
    }

    [Test]
    public async Task WriteString_Null_ThrowsKeyFormatException()
    {
        // Arrange
        // Act
        var act = () => Write((ref KeyWriter writer) => writer.WriteString(null!, "#"u8, Name));

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(Name);
    }

    [Test]
    public async Task WriteString_IllFormedTextBeyondTheBuffer_ThrowsKeyFormatException()
    {
        // Arrange (the lone surrogate comes after the point where the buffer is full)
        var value = "abcdefgh\uD800";

        // Act
        var act = () => Write((ref KeyWriter writer) => writer.WriteString(value, "#"u8, Name), 4);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(Name);
    }

    [Test]
    public async Task WriteString_ExactlyAsLongAsTheBuffer_NeedsNoPool()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteString("abcd", "#"u8, Name), 4, pool);

        // Assert
        await Assert.That((Encoding.UTF8.GetString(bytes), pool.RentedLengths.Count)).IsEqualTo(("abcd", 0));
    }

    [Test]
    public async Task WriteString_ValueWithAMultiByteCharacterOverTheBufferEdge_WritesTheWholeCharacter()
    {
        // Arrange (the character takes bytes 3 to 5 of a 4-byte buffer)
        var value = "ab€c";

        // Act
        var text = Text((ref KeyWriter writer) => writer.WriteString(value, "#"u8, Name), 4);

        // Assert
        await Assert.That(text).IsEqualTo(value);
    }

    [Test]
    public async Task WriteTimestamp_LocalDateTime_WritesTheUtcInstant()
    {
        // Arrange
        var local = new DateTime(2026, 6, 15, 12, 30, 45, 123, DateTimeKind.Local);
        var utc = local.ToUniversalTime();
        var expected = $"{utc.Year:D4}-{utc.Month:D2}-{utc.Day:D2}T{utc.Hour:D2}:{utc.Minute:D2}:{utc.Second:D2}.{utc.Millisecond:D3}Z";

        // Act
        var text = Text((ref KeyWriter writer) => writer.WriteTimestamp(local, KeyTimeFormat.Millisecond, Name));

        // Assert
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(UndefinedTimeFormats))]
    public async Task WriteTimestamp_UndefinedFormat_ThrowsArgumentOutOfRangeException(int format)
    {
        // Arrange
        // Act
        var forOffset = () => Write((ref KeyWriter writer) => writer.WriteTimestamp(DateTimeOffset.UnixEpoch, (KeyTimeFormat)format));
        var forDateTime = () => Write((ref KeyWriter writer) => writer.WriteTimestamp(DateTime.UnixEpoch, (KeyTimeFormat)format, Name));

        // Assert
        await Assert.That(forOffset).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(forDateTime).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [MethodDataSource(nameof(InvalidFixedPointFormats))]
    public async Task WriteFixedPoint_InvalidFormat_ThrowsArgumentOutOfRangeException((int IntegerDigits, int FractionDigits) format)
    {
        // Arrange
        // Act
        var act = () => Write((ref KeyWriter writer) => writer.WriteFixedPoint(1m, format.IntegerDigits, format.FractionDigits, Name));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task WriteFormattable_TypeThatOutgrowsTheBuffer_GrowsUntilItFits()
    {
        // Arrange
        var value = new RawText(new string('x', 1000));
        var pool = new CountingArrayPool<byte>();

        // Act
        var bytes = KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteFormattable(value, "#"u8, Name), 10, pool);

        // Assert
        await Assert.That((bytes.Length, pool.RentedLengths.Count)).IsEqualTo((1000, 4));
    }

    [Test]
    public async Task WriteFormattable_TypeThatFitsAfterLiteralsOfTheBuffer_WritesAfterThem()
    {
        // Arrange
        var value = new RawText("tail");

        // Act
        var text = Text((ref KeyWriter writer) =>
        {
            writer.WriteLiteral("head#"u8);
            writer.WriteFormattable(value, "#"u8, Name);
        });

        // Assert
        await Assert.That(text).IsEqualTo("head#tail");
    }

    [Test]
    public async Task WriteFormattable_TextOfExactlyTheLimit_IsWritten()
    {
        // Arrange
        var value = new RawText(new string('x', 64 * 1024));

        // Act
        var bytes = Write((ref KeyWriter writer) => writer.WriteFormattable(value, "#"u8, Name), 10);

        // Assert
        await Assert.That(bytes.Length).IsEqualTo(64 * 1024);
    }

    [Test]
    public async Task WriteFormattable_TextOverTheLimit_ThrowsKeyFormatExceptionWithoutRentingMoreThanTheLimit()
    {
        // Arrange
        var value = new RawText(new string('x', (64 * 1024) + 1));
        var pool = new CountingArrayPool<byte>();

        // Act
        var act = () => KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteFormattable(value, "#"u8, Name), 10, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(Name);
        await Assert.That((pool.RentedLengths.Max() <= 64 * 1024, pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task WriteFormattable_TypeThatNeverFits_ThrowsKeyFormatException()
    {
        // Arrange
        var value = default(NeverFits);

        // Act
        var act = () => Write((ref KeyWriter writer) => writer.WriteFormattable(value, "#"u8, Name), 100_000);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(Name);
    }

    [Test]
    [Arguments("a#b")]
    [Arguments("a\u0001b")]
    [Arguments("")]
    public async Task WriteFormattable_TextThatIsNotASegment_ThrowsKeyFormatException(string text)
    {
        // Arrange
        var value = new RawText(text);

        // Act
        var act = () => Write((ref KeyWriter writer) => writer.WriteFormattable(value, "#"u8, Name));

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(Name);
    }

    [Test]
    public async Task WriteFormattable_IllFormedUtf8_ThrowsKeyFormatException()
    {
        // Arrange
        var value = new RawBytes([0x61, 0xC3]);

        // Act
        var act = () => Write((ref KeyWriter writer) => writer.WriteFormattable(value, "#"u8, Name));

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(Name);
    }

    [Test]
    public async Task WriteUtf8Value_IllFormedUtf8_ThrowsKeyFormatException()
    {
        // Arrange
        // Act
        var act = () => Write((ref KeyWriter writer) => writer.WriteUtf8Value([0xED, 0xA0, 0x80], "#"u8, Name));

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(Name);
    }

    [Test]
    public async Task WriteDigits_ValueExactlyAsWideAsTheFormat_NeedsNoPadding()
    {
        // Arrange
        // Act
        var text = Text((ref KeyWriter writer) => writer.WriteDigits(4711, 4, Name));

        // Assert
        await Assert.That(text).IsEqualTo("4711");
    }

    [Test]
    [MethodDataSource(nameof(FailingWrites))]
    public async Task Write_NotThrowingAndAValueThatWouldThrow_MarksTheWriterAndLeavesItAsItWas(FailingWrite failing)
    {
        // Arrange, Act
        var result = WriteWithoutThrowing(failing.Action);

        // Assert
        await Assert.That(result).IsEqualTo((true, "x"));
    }

    [Test]
    [MethodDataSource(nameof(RejectedWrites))]
    public async Task Write_NotThrowingAndAVectorTheOracleRejects_MarksTheWriter(KeyVector vector)
    {
        // Arrange, Act
        var (isInvalid, _) = WriteWithoutThrowing((ref KeyWriter writer) => KeyVectorRunner.Apply(vector, ref writer));

        // Assert
        await Assert.That(isInvalid).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(AcceptedWrites))]
    public async Task Write_NotThrowingAndAVectorTheOracleAccepts_WritesWhatTheThrowingWriterWrites(KeyVector vector)
    {
        // Arrange
        var expected = Encoding.UTF8.GetString(KeyVectorRunner.Write(vector, 512));

        // Act
        var result = WriteWithoutThrowing((ref KeyWriter writer) => KeyVectorRunner.Apply(vector, ref writer));

        // Assert
        await Assert.That(result).IsEqualTo((false, "x" + expected));
    }

    [Test]
    public async Task IsInvalid_WritesAfterARejectedValue_StaysTrue()
    {
        // Arrange, Act
        var (isInvalid, _) = WriteWithoutThrowing(static (ref KeyWriter writer) =>
        {
            writer.WriteString(string.Empty, default, Name);
            writer.WriteLiteral("#"u8);
            writer.WriteString("fine", default, Name);
        });

        // Assert
        await Assert.That(isInvalid).IsTrue();
    }

    [Test]
    public async Task IsInvalid_NothingRejected_IsFalse()
    {
        // Arrange, Act
        var (isInvalid, _) = WriteWithoutThrowing(static (ref KeyWriter writer) => writer.WriteString("fine", default, Name));

        // Assert
        await Assert.That(isInvalid).IsFalse();
    }

    private static (bool IsInvalid, string Written) WriteWithoutThrowing(KeyWriterAction action)
    {
        var writer = new KeyWriter(stackalloc byte[512], throwOnInvalid: false);
        try
        {
            writer.WriteLiteral("x"u8);
            action(ref writer);
            return (writer.IsInvalid, writer.ToString());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static byte[] Write(KeyWriterAction action, int bufferLength = 512) => KeyVectorRunner.Write(action, bufferLength);

    private static string Text(KeyWriterAction action, int bufferLength = 512) => Encoding.UTF8.GetString(Write(action, bufferLength));

    private static void TryWrite(ref KeyWriter writer, string value)
    {
        try
        {
            writer.WriteString(value, "#"u8, Name);
        }
        catch (KeyFormatException)
        {
            // The point of the test: the writer is as it was.
        }
    }
}
