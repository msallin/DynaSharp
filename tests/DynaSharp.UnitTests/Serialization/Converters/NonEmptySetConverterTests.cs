using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class NonEmptySetConverterTests
{
    [Test]
    public async Task Write_EmptySet_ThrowsTheRefusalThatTheGeneratedWriteNames()
    {
        // Arrange (a set in a list or a map cannot be left out, and DynamoDB rejects it empty)
        // Act
        var act = () => ConverterRun.Write<HashSet<string>, NonEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>>([]);

        // Assert
        await Assert.That(act).ThrowsExactly<EmptySetException>()
            .WithMessage("A DynamoDB set cannot be empty, and DynamoDB rejects one also inside a list or a map. Leave the empty set out, or store its elements as a list. (Parameter 'value')", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_Set_WritesWhatTheSetConverterWrites()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<HashSet<string>, NonEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>>(["a"]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["a"]}""");
    }

    [Test]
    public async Task Write_Null_ThrowsWhatTheSetConverterThrows()
    {
        // Arrange
        HashSet<string> value = null!;

        // Act
        var act = () => ConverterRun.Write<HashSet<string>, NonEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Read_Set_ReadsWhatTheSetConverterReads()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<HashSet<int>, NonEmptySetConverter<HashSet<int>, int, HashSetConverter<int, NumberSetElement<int>>>>("""{"NS":["2","1"]}""");

        // Assert
        await Assert.That(value.Order()).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task TryReadMissing_Always_GivesAnEmptySetAsTheSetConverterDoes()
    {
        // Arrange
        // Act
        var found = NonEmptySetConverter<HashSet<string>, string, HashSetConverter<string, StringSetElement>>.TryReadMissing(out var value);

        // Assert
        await Assert.That((found, value.Count)).IsEqualTo((true, 0));
    }
}
