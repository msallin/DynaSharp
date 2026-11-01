using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>
/// Pins <see cref="KeyWriter"/> and <see cref="KeyReader"/> to the golden vectors of <c>generate_key_vectors.py</c>. The expected bytes
/// and verdicts come from Python (decimal, datetime, uuid, strict codecs), not from this library.
/// </summary>
public sealed class KeyVectorTests
{
    private const int RoomyBuffer = 512;
    private const int NoBuffer = 0;

    public static IEnumerable<KeyVector> AcceptedWrites() => KeyVectors.Writes(error: false);

    public static IEnumerable<KeyVector> RejectedWrites() => KeyVectors.Writes(error: true);

    public static IEnumerable<KeyVector> AcceptedReads() => KeyVectors.Reads(accept: true);

    public static IEnumerable<KeyVector> RejectedReads() => KeyVectors.Reads(accept: false);

    public static IEnumerable<KeyVector> AcceptedTimestampReads() => KeyVectors.ReadsOfKind("timestamp", accept: true);

    public static IEnumerable<KeyVector> WrittenComposites() => KeyVectors.Composites(error: false);

    public static IEnumerable<KeyVector> RejectedComposites() => KeyVectors.Composites(error: true);

    public static IEnumerable<KeyVector> AcceptedCompositeReads() => KeyVectors.CompositeReads(accept: true);

    public static IEnumerable<KeyVector> RejectedCompositeReads() => KeyVectors.CompositeReads(accept: false);

    [Test]
    [MethodDataSource(nameof(AcceptedWrites))]
    public async Task Write_VectorTheOracleEncodes_ProducesTheBytesOfTheOracleWithAndWithoutRoomInTheBuffer(KeyVector vector)
    {
        // Arrange
        var expected = Convert.ToHexString(vector.GetExpectedBytes());

        // Act
        var bytes = (Roomy: KeyVectorRunner.Write(vector, RoomyBuffer), Tight: KeyVectorRunner.Write(vector, NoBuffer));

        // Assert
        await Assert.That((Convert.ToHexString(bytes.Roomy), Convert.ToHexString(bytes.Tight))).IsEqualTo((expected, expected));
    }

    [Test]
    [MethodDataSource(nameof(RejectedWrites))]
    public async Task Write_VectorTheOracleRejects_ThrowsKeyFormatExceptionNamingThePlaceholder(KeyVector vector)
    {
        // Arrange (a buffer with room: rejection must not depend on growth)
        // Act
        var act = () => KeyVectorRunner.Write(vector, RoomyBuffer);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(KeyVectorRunner.Placeholder);
    }

    [Test]
    [MethodDataSource(nameof(AcceptedReads))]
    public async Task Read_VectorTheOracleAccepts_ReturnsTheValueOfTheOracle(KeyVector vector)
    {
        // Arrange
        var expected = KeyVectorRunner.ExpectedValue(vector);

        // Act
        var value = KeyVectorRunner.Read(vector);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(RejectedReads))]
    public async Task Read_VectorTheOracleRejects_FailsOrLeavesInput(KeyVector vector)
    {
        // Arrange (the oracle rejects a candidate that Format(Parse(x)) would not reproduce)

        // Act
        var value = KeyVectorRunner.Read(vector);

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(AcceptedTimestampReads))]
    public async Task TryReadTimestamp_AcceptedVectorAsDateTimeOffset_ReturnsTheSameInstantWithOffsetZero(KeyVector vector)
    {
        // Arrange
        var expectedTicks = (long)KeyVectorRunner.ExpectedValue(vector);

        // Act
        var (read, value) = ReadOffset(vector);

        // Assert
        await Assert.That((read, value.UtcTicks, value.Offset)).IsEqualTo((true, expectedTicks, TimeSpan.Zero));
    }

    [Test]
    [MethodDataSource(nameof(WrittenComposites))]
    public async Task Write_CompositeVector_ProducesTheKeyOfTheOracleWithAndWithoutRoomInTheBuffer(KeyVector vector)
    {
        // Arrange
        var expected = Convert.ToHexString(vector.GetExpectedBytes());

        // Act
        var bytes = (Roomy: KeyVectorRunner.WriteComposite(vector, RoomyBuffer), Tight: KeyVectorRunner.WriteComposite(vector, NoBuffer));

        // Assert
        await Assert.That((Convert.ToHexString(bytes.Roomy), Convert.ToHexString(bytes.Tight))).IsEqualTo((expected, expected));
    }

    [Test]
    [MethodDataSource(nameof(RejectedComposites))]
    public async Task Write_CompositeVectorWithADelimiterOrAnInvalidValue_ThrowsKeyFormatException(KeyVector vector)
    {
        // Arrange
        // Act
        var act = () => KeyVectorRunner.WriteComposite(vector, RoomyBuffer);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>().WithParameterName(KeyVectorRunner.Placeholder);
    }

    [Test]
    [MethodDataSource(nameof(WrittenComposites))]
    public async Task ReadComposite_KeyTheOracleWrote_ReturnsTheValuesThatWereWritten(KeyVector vector)
    {
        // Arrange
        var expected = KeyVectorRunner.ExpectedValues(vector);

        // Act
        var values = KeyVectorRunner.ReadComposite(vector, vector.GetExpectedBytes());

        // Assert
        await Assert.That(values).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(AcceptedCompositeReads))]
    public async Task ReadComposite_KeyTheOracleAccepts_ReadsEveryPlaceholder(KeyVector vector)
    {
        // Arrange (a key that the oracle accepts also reads back to itself)

        // Act
        var values = KeyVectorRunner.ReadComposite(vector, vector.GetInput());

        // Assert
        await Assert.That(values).IsNotNull();
    }

    [Test]
    [MethodDataSource(nameof(RejectedCompositeReads))]
    public async Task ReadComposite_KeyTheOracleRejects_Fails(KeyVector vector)
    {
        // Arrange (the delimiters are those the oracle derived from the template)

        // Act
        var values = KeyVectorRunner.ReadComposite(vector, vector.GetInput());

        // Assert
        await Assert.That(values).IsNull();
    }

    private static (bool Read, DateTimeOffset Value) ReadOffset(KeyVector vector)
    {
        var reader = new KeyReader(vector.GetInput());
        var read = reader.TryReadTimestamp(vector.Format, out DateTimeOffset value);
        return (read, value);
    }
}
