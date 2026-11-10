using DynaSharp.Serialization;
using DynaSharp.UnitTests.Properties;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// What a converter writes, it reads back as the same value, over the whole domain of its type: text of every plane, numbers of every magnitude and
/// precision, times at the limits of their range. The examples of each converter's own tests pin the text; these pin that nothing is lost on the way.
/// </summary>
public sealed class ConverterPropertyTests
{
    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyValidText_GivesTheSameString(string value)
    {
        // Arrange (the text is any valid UTF-16, control characters, quotes and supplementary planes included)
        // Act
        var back = ConverterRun.Read<string, StringConverter>(ConverterRun.Write<string, StringConverter>(value));

        // Assert
        await Assert.That(back).IsEqualTo(value);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task Write_TextWithALoneSurrogate_ThrowsArgumentException(LoneSurrogateText text)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Write<string, StringConverter>(text.Value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyInt32_GivesTheSameNumber(int value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<int, NumberConverter<int>>(ConverterRun.Write<int, NumberConverter<int>>(value));

        // Assert
        await Assert.That(back).IsEqualTo(value);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyInt64_GivesTheSameNumber(long value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<long, NumberConverter<long>>(ConverterRun.Write<long, NumberConverter<long>>(value));

        // Assert
        await Assert.That(back).IsEqualTo(value);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyUInt64_GivesTheSameNumber(ulong value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<ulong, NumberConverter<ulong>>(ConverterRun.Write<ulong, NumberConverter<ulong>>(value));

        // Assert
        await Assert.That(back).IsEqualTo(value);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyDecimal_GivesTheSameNumberWithItsScale(decimal value)
    {
        // Arrange (any coefficient up to 96 bits at any scale up to 28: up to 29 significant digits)
        // Act
        var back = ConverterRun.Read<decimal, NumberConverter<decimal>>(ConverterRun.Write<decimal, NumberConverter<decimal>>(value));

        // Assert
        await Assert.That((back, back.Scale)).IsEqualTo((value, value.Scale));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyFiniteDouble_GivesTheSameBits(double value)
    {
        // Arrange (any bit pattern that is finite: subnormals and negative zero included)
        // Act
        var back = ConverterRun.Read<double, NumberConverter<double>>(ConverterRun.Write<double, NumberConverter<double>>(value));

        // Assert
        await Assert.That(BitConverter.DoubleToInt64Bits(back)).IsEqualTo(BitConverter.DoubleToInt64Bits(value));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyFiniteSingle_GivesTheSameBits(float value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<float, NumberConverter<float>>(ConverterRun.Write<float, NumberConverter<float>>(value));

        // Assert
        await Assert.That(BitConverter.SingleToInt32Bits(back)).IsEqualTo(BitConverter.SingleToInt32Bits(value));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyUtcDateTime_GivesTheSameTicksInUtc(DateTime value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<DateTime, DateTimeConverter>(ConverterRun.Write<DateTime, DateTimeConverter>(value));

        // Assert
        await Assert.That((back.Ticks, back.Kind)).IsEqualTo((value.Ticks, DateTimeKind.Utc));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyDateTimeOffset_GivesTheSameInstantInUtc(DateTimeOffset value)
    {
        // Arrange (any instant at any offset)
        // Act
        var back = ConverterRun.Read<DateTimeOffset, DateTimeOffsetConverter>(ConverterRun.Write<DateTimeOffset, DateTimeOffsetConverter>(value));

        // Assert
        await Assert.That((back.UtcTicks, back.Offset)).IsEqualTo((value.UtcTicks, TimeSpan.Zero));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyDateTimeOffsetWithItsOffset_GivesTheSameTimeAndOffset(DateTimeOffset value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<DateTimeOffset, OffsetTimestampConverter>(ConverterRun.Write<DateTimeOffset, OffsetTimestampConverter>(value));

        // Assert
        await Assert.That(back.EqualsExact(value)).IsTrue();
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyDateTimeOffsetInEpochSeconds_GivesTheInstantCutToTheSecond(DateTimeOffset value)
    {
        // Arrange
        var second = new DateTimeOffset(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        // Act
        var back = ConverterRun.Read<DateTimeOffset, EpochSecondsConverter>(ConverterRun.Write<DateTimeOffset, EpochSecondsConverter>(value));

        // Assert
        await Assert.That(back.EqualsExact(second)).IsTrue();
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyTimeSpan_GivesTheSameTicks(TimeSpan value)
    {
        // Arrange (negative spans and both limits included)
        // Act
        var back = ConverterRun.Read<TimeSpan, TimeSpanConverter>(ConverterRun.Write<TimeSpan, TimeSpanConverter>(value));

        // Assert
        await Assert.That(back.Ticks).IsEqualTo(value.Ticks);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyDateOnly_GivesTheSameDay(DateOnly value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<DateOnly, DateOnlyConverter>(ConverterRun.Write<DateOnly, DateOnlyConverter>(value));

        // Assert
        await Assert.That(back).IsEqualTo(value);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyTimeOnly_GivesTheSameTicks(TimeOnly value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<TimeOnly, TimeOnlyConverter>(ConverterRun.Write<TimeOnly, TimeOnlyConverter>(value));

        // Assert
        await Assert.That(back.Ticks).IsEqualTo(value.Ticks);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyGuid_GivesTheSameGuid(Guid value)
    {
        // Arrange
        // Act
        var back = ConverterRun.Read<Guid, GuidConverter>(ConverterRun.Write<Guid, GuidConverter>(value));

        // Assert
        await Assert.That(back).IsEqualTo(value);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyBytes_GivesTheSameBytes(byte[] value)
    {
        // Arrange (possibly none)
        // Act
        var back = ConverterRun.Read<byte[], BinaryConverter>(ConverterRun.Write<byte[], BinaryConverter>(value));

        // Assert
        await Assert.That(Convert.ToHexString(back)).IsEqualTo(Convert.ToHexString(value));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyListOfText_GivesTheSameElementsInOrder(string[] values)
    {
        // Arrange
        var list = values.ToList();

        // Act
        var back = ConverterRun.Read<List<string>, ListConverter<string, StringConverter>>(ConverterRun.Write<List<string>, ListConverter<string, StringConverter>>(list));

        // Assert
        await Assert.That(back).IsEquivalentTo(list, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnyDictionaryOfNumbers_GivesTheSameEntries(string[] names, long[] numbers)
    {
        // Arrange (names of any text but the empty one, which the converter refuses as DynamoDB does)
        var values = names.Where(static name => name.Length > 0).Distinct(StringComparer.Ordinal).Zip(numbers).ToDictionary(StringComparer.Ordinal);

        // Act
        var back = ConverterRun.Read<Dictionary<string, long>, DictionaryConverter<long, NumberConverter<long>>>(ConverterRun.Write<Dictionary<string, long>, DictionaryConverter<long, NumberConverter<long>>>(values));

        // Assert
        await Assert.That(back.OrderBy(static entry => entry.Key, StringComparer.Ordinal).SequenceEqual(values.OrderBy(static entry => entry.Key, StringComparer.Ordinal))).IsTrue();
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnySetOfText_GivesTheSameSet(string[] members)
    {
        // Arrange
        var values = members.ToHashSet(StringComparer.Ordinal);

        // Act
        var back = ConverterRun.Read<HashSet<string>, HashSetConverter<string, StringSetElement>>(ConverterRun.Write<HashSet<string>, HashSetConverter<string, StringSetElement>>(values));

        // Assert
        await Assert.That(back.SetEquals(values) && back.Count == values.Count).IsTrue();
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnySetOfDecimals_GivesTheSameSet(decimal[] members)
    {
        // Arrange (equal numbers of different scales are one member)
        var values = members.ToHashSet();

        // Act
        var back = ConverterRun.Read<HashSet<decimal>, HashSetConverter<decimal, NumberSetElement<decimal>>>(ConverterRun.Write<HashSet<decimal>, HashSetConverter<decimal, NumberSetElement<decimal>>>(values));

        // Assert
        await Assert.That(back.SetEquals(values) && back.Count == values.Count).IsTrue();
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries)])]
    public async Task RoundTrip_AnySetOfGuids_GivesTheSameSet(Guid[] members)
    {
        // Arrange
        var values = members.ToHashSet();

        // Act
        var back = ConverterRun.Read<HashSet<Guid>, HashSetConverter<Guid, GuidSetElement>>(ConverterRun.Write<HashSet<Guid>, HashSetConverter<Guid, GuidSetElement>>(values));

        // Assert
        await Assert.That(back.SetEquals(values) && back.Count == values.Count).IsTrue();
    }
}
